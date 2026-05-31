using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Nagi.Core.Services.Abstractions;
using Nagi.WinUI.Models;
using Nagi.WinUI.Services.Abstractions;

namespace Nagi.WinUI.Services.Implementations;

public sealed class DownloadService : IDownloadService
{
    // Matches [download] 75.3% or [download]  75.3%
    private static readonly Regex _progressRegex = new(@"\[download\]\s+([\d.]+)%", RegexOptions.Compiled);

    // Matches [download] Destination: .../Artist - Title.webm  OR
    //         [ExtractAudio] Destination: .../Artist - Title.mp3
    // Captures the filename stem (without extension).
    private static readonly Regex _destinationRegex = new(
        @"\[(?:download|ExtractAudio)\] Destination: .+[\\/](.+?)\.\w+$",
        RegexOptions.Compiled);

    private readonly ILibraryScanner _libraryScanner;
    private readonly ILibraryReader _libraryReader;
    private readonly ILogger<DownloadService> _logger;
    private readonly DispatcherQueue _dispatcherQueue;

    public DownloadService(
        ILibraryScanner libraryScanner,
        ILibraryReader libraryReader,
        ILogger<DownloadService> logger,
        DispatcherQueue dispatcherQueue)
    {
        _libraryScanner = libraryScanner;
        _libraryReader = libraryReader;
        _logger = logger;
        _dispatcherQueue = dispatcherQueue;
    }

    public ObservableCollection<DownloadItem> Downloads { get; } = new();

    public async Task DownloadUrlAsync(string url, string musicFolder, CancellationToken ct = default)
    {
        var isSoundCloud = url.Contains("soundcloud.com", StringComparison.OrdinalIgnoreCase);
        var source = isSoundCloud ? DownloadSource.SoundCloud : DownloadSource.YouTube;

        var item = new DownloadItem(Guid.NewGuid().ToString(), url, source)
        {
            Status = DownloadStatus.Pending
        };
        Downloads.Add(item);

        var ok = await RunYtDlpAsync(url, musicFolder, item, ct);

        if (!ok && isSoundCloud && !ct.IsCancellationRequested)
        {
            var query = BuildYouTubeQueryFromSoundCloudUrl(url);
            var fallbackItem = new DownloadItem(Guid.NewGuid().ToString(), query, DownloadSource.YouTube)
            {
                Status = DownloadStatus.Pending
            };
            _dispatcherQueue.TryEnqueue(() => item.StatusMessage = "Trying YouTube fallback…");
            Downloads.Add(fallbackItem);
            await RunYtDlpAsync($"ytsearch1:{query}", musicFolder, fallbackItem, ct);
        }

        _ = TriggerRescanAsync(musicFolder);
    }

    public async Task SyncSoundCloudLikesAsync(
        string username,
        string authToken,
        string clientId,
        string musicFolder,
        CancellationToken ct = default)
    {
        var syncItem = new DownloadItem(
            Guid.NewGuid().ToString(),
            $"SoundCloud Likes – {username}",
            DownloadSource.SoundCloud)
        {
            Status = DownloadStatus.Downloading
        };
        Downloads.Add(syncItem);

        var archivePath = Path.Combine(musicFolder, "archive.txt");
        var scdlArgs = new[]
        {
            "-l", $"https://soundcloud.com/{username}/likes",
            "--auth-token", authToken,
            "--client-id", clientId,
            "--path", musicFolder,
            "--download-archive", archivePath,
            "--yt-dlp-args", "--sleep-requests 3 --sleep-interval 3 --max-sleep-interval 6"
        };

        // Collect 404 fallbacks during the scdl run; process them afterward
        // so we don't need async inside the sync onLine callback.
        var fallbackQueue = new Queue<string>();
        string? lastExtractedUrl = null;

        await RunProcessAsync("scdl", scdlArgs, line =>
        {
            if (line.Contains("Extracting URL:"))
            {
                var m = Regex.Match(line, @"Extracting URL:\s*(\S+)");
                if (m.Success) lastExtractedUrl = m.Groups[1].Value;
            }

            if (line.Contains("HTTP Error 404") && lastExtractedUrl != null)
            {
                fallbackQueue.Enqueue(lastExtractedUrl);
                lastExtractedUrl = null;
            }

            var msg = line.Length > 80 ? line[..80] : line;
            _dispatcherQueue.TryEnqueue(() => syncItem.StatusMessage = msg);
        }, _ => { }, ct);

        foreach (var failedUrl in fallbackQueue)
        {
            if (ct.IsCancellationRequested) break;

            var query = BuildYouTubeQueryFromSoundCloudUrl(failedUrl);
            var trackId = ExtractTrackIdFromSoundCloudUrl(failedUrl);
            var fallbackItem = new DownloadItem(
                Guid.NewGuid().ToString(), query, DownloadSource.YouTube)
            { Status = DownloadStatus.Pending };
            Downloads.Add(fallbackItem);

            var ok = await RunYtDlpAsync($"ytsearch1:{query}", musicFolder, fallbackItem, ct);
            if (ok && trackId != null)
                AppendLineToFile(archivePath, $"soundcloud {trackId}");
            else if (!ok)
                AppendLineToFile(Path.Combine(musicFolder, "lost.txt"), failedUrl);
        }

        var finalStatus = ct.IsCancellationRequested ? DownloadStatus.Failed : DownloadStatus.Complete;
        _dispatcherQueue.TryEnqueue(() => syncItem.Status = finalStatus);
        _ = TriggerRescanAsync(musicFolder);
    }

    private async Task<bool> RunYtDlpAsync(
        string url, string musicFolder, DownloadItem item, CancellationToken ct)
    {
        _dispatcherQueue.TryEnqueue(() => item.Status = DownloadStatus.Downloading);

        var outputTemplate = Path.Combine(musicFolder, "%(uploader)s - %(title)s.%(ext)s");
        var args = new[]
        {
            url,
            "--no-playlist",
            "-x",
            "--audio-format", "mp3",
            "--audio-quality", "0",
            "--embed-thumbnail",
            "--embed-metadata",
            "--restrict-filenames",
            "--newline",
            "--progress",
            "--no-overwrites",
            "-o", outputTemplate
        };

        var success = false;
        await RunProcessAsync("yt-dlp", args, line =>
        {
            Debug.WriteLine($"[yt-dlp] {line}");
            _logger.LogTrace("[yt-dlp] {Line}", line);

            var destMatch = _destinationRegex.Match(line);
            if (destMatch.Success)
            {
                var title = destMatch.Groups[1].Value;
                _dispatcherQueue.TryEnqueue(() => item.Title = title);
            }

            var progressMatch = _progressRegex.Match(line);
            if (progressMatch.Success &&
                double.TryParse(progressMatch.Groups[1].Value,
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var pct))
                _dispatcherQueue.TryEnqueue(() => item.Progress = pct);

            if (line.Contains("[ExtractAudio]"))
                _dispatcherQueue.TryEnqueue(() => item.Progress = 100);

            var msg = line.Length > 80 ? line[..80] : line;
            _dispatcherQueue.TryEnqueue(() => item.StatusMessage = msg);
        }, exitCode =>
        {
            success = exitCode == 0;
            var status = success ? DownloadStatus.Complete : DownloadStatus.Failed;
            _dispatcherQueue.TryEnqueue(() =>
            {
                item.Status = status;
                if (success) item.Progress = 100;
            });
        }, ct);

        return success;
    }

    private async Task RunProcessAsync(
        string executable,
        string[] args,
        Action<string> onLine,
        Action<int> onExit,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardInput = true,  // must redirect so we can close it immediately
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) onLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) onLine(e.Data); // yt-dlp writes progress to stderr in newer builds
        };

        ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
        });

        if (!process.Start())
        {
            _logger.LogError("Failed to start process: {Exe}", executable);
            onExit(-1);
            return;
        }

        // Close stdin immediately. ffmpeg (spawned by yt-dlp) inherits the stdin handle and,
        // when it is open but not a terminal, enters a mode where it reads stdin for interactive
        // commands (q = quit, p = pause). Sending EOF here prevents it from ever blocking on
        // a stdin read, which is what caused the hang during the embed-thumbnail/embed-metadata
        // post-processing steps.
        process.StandardInput.Close();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            onExit(process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Process cancelled: {Exe}", executable);
            onExit(-1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Process failed unexpectedly: {Exe}", executable);
            onExit(-1);
        }
    }

    private async Task TriggerRescanAsync(string musicFolder)
    {
        // yt-dlp exits after ffmpeg closes the output file, but Windows (Defender,
        // Search Indexer, shell thumbnail provider) can briefly re-open a newly
        // created file. The scanner's one 500 ms FileAccessError retry is not
        // always enough. A short settle delay here ensures the file handle is
        // fully released before the scanner tries to read its ID3 metadata.
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        try
        {
            // GetAllFoldersAsync returns every row in the Folders table, including
            // auto-created subfolder entries (ParentFolderId != null) produced by
            // EnsureSubFoldersExistAsync. Those entries never own songs directly —
            // songs are stored under the root folder's FolderId. Scanning with a
            // subfolder entry's ID would see an empty dbFileMap, treat all 6000+
            // existing disk files as new additions, and blow up on the FilePath
            // UNIQUE constraint. Only root folders (ParentFolderId == null) are
            // valid scan targets.
            var allFolders = await _libraryReader.GetAllFoldersAsync(CancellationToken.None).ConfigureAwait(false);
            var rootFolders = allFolders.Where(f => f.IsRootFolder).ToList();

            _logger.LogInformation(
                "TriggerRescanAsync: download folder='{MusicFolder}', root folders=[{Folders}]",
                musicFolder,
                string.Join(", ", rootFolders.Select(f => f.Path)));

            // Find the deepest ROOT folder that contains the download folder.
            // Exact match wins; StartsWith handles downloads into a subdirectory
            // of a registered root (e.g. Music\SoundCloud\ inside Music\).
            var normalizedMusic = musicFolder.TrimEnd('\\', '/');
            var match = rootFolders
                .Where(f =>
                {
                    var normalizedRegistered = f.Path.TrimEnd('\\', '/');
                    return string.Equals(normalizedMusic, normalizedRegistered, StringComparison.OrdinalIgnoreCase)
                        || normalizedMusic.StartsWith(
                            normalizedRegistered + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(f => f.Path.Length) // deepest root ancestor wins
                .FirstOrDefault();

            if (match is null)
            {
                _logger.LogWarning(
                    "TriggerRescanAsync: no registered folder contains '{MusicFolder}'. Rescan skipped.",
                    musicFolder);
                return;
            }

            _logger.LogDebug(
                "TriggerRescanAsync: matched '{FolderPath}' (ID={FolderId}). Starting rescan.",
                match.Path, match.Id);

            // CancellationToken.None: the download's token must not be able to abort the
            // library scan that follows — if another scan holds the semaphore, we wait
            // unconditionally rather than silently dropping the rescan.
            await _libraryScanner.RescanFolderForMusicAsync(match.Id, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            _logger.LogDebug("TriggerRescanAsync: rescan of '{FolderPath}' complete.", match.Path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Library rescan after download failed (non-critical).");
        }
    }

    private static string BuildYouTubeQueryFromSoundCloudUrl(string soundCloudUrl)
    {
        var slug = Uri.TryCreate(soundCloudUrl, UriKind.Absolute, out var uri)
            ? uri.Segments.LastOrDefault() ?? soundCloudUrl
            : soundCloudUrl;

        return slug.Replace("-", " ").Replace("_", " ").Trim('/').Trim();
    }

    private static string? ExtractTrackIdFromSoundCloudUrl(string soundCloudUrl)
    {
        return Uri.TryCreate(soundCloudUrl, UriKind.Absolute, out var uri)
            ? uri.Segments.LastOrDefault()?.Trim('/')
            : null;
    }

    private static void AppendLineToFile(string path, string line)
    {
        try { File.AppendAllText(path, line + Environment.NewLine); }
        catch { }
    }
}
