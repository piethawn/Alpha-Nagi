using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nagi.Core.Services.Abstractions;
using Nagi.WinUI.Models;
using Nagi.WinUI.Resources;
using Nagi.WinUI.Services.Abstractions;

namespace Nagi.WinUI.ViewModels;

public partial class DownloadsViewModel : ObservableObject
{
    private readonly IDownloadService _downloadService;
    private readonly ILibraryReader _libraryReader;
    private readonly IUISettingsService _settingsService;
    private readonly ILogger<DownloadsViewModel> _logger;

    private CancellationTokenSource? _activeCts;

    public DownloadsViewModel(
        IDownloadService downloadService,
        ILibraryReader libraryReader,
        IUISettingsService settingsService,
        ILogger<DownloadsViewModel> logger)
    {
        _downloadService = downloadService;
        _libraryReader = libraryReader;
        _settingsService = settingsService;
        _logger = logger;
    }

    [ObservableProperty] public partial string UrlInput { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsDownloading { get; set; }
    [ObservableProperty] public partial bool IsSyncing { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;

    public ObservableCollection<DownloadItem> Downloads => _downloadService.Downloads;

    [RelayCommand]
    private async Task DownloadAsync()
    {
        var url = UrlInput?.Trim();
        if (string.IsNullOrEmpty(url))
        {
            StatusMessage = Resources.Strings.Downloads_Error_EmptyUrl;
            return;
        }

        var musicFolder = await GetPrimaryMusicFolderAsync();
        if (musicFolder == null)
        {
            StatusMessage = Resources.Strings.Downloads_Error_NoMusicFolder;
            return;
        }

        IsDownloading = true;
        StatusMessage = string.Empty;
        _activeCts = new CancellationTokenSource();

        try
        {
            UrlInput = string.Empty;
            await _downloadService.DownloadUrlAsync(url, musicFolder, _activeCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed for URL {Url}", url);
            StatusMessage = ex.Message;
        }
        finally
        {
            IsDownloading = false;
            _activeCts?.Dispose();
            _activeCts = null;
        }
    }

    [RelayCommand]
    private async Task SyncSoundCloudAsync()
    {
        var username = await _settingsService.GetSoundCloudUsernameAsync();
        var authToken = await _settingsService.GetSoundCloudAuthTokenAsync();
        var clientId = await _settingsService.GetSoundCloudClientIdAsync();

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(authToken) || string.IsNullOrEmpty(clientId))
        {
            StatusMessage = Resources.Strings.Downloads_Error_MissingCredentials;
            return;
        }

        var musicFolder = await GetPrimaryMusicFolderAsync();
        if (musicFolder == null)
        {
            StatusMessage = Resources.Strings.Downloads_Error_NoMusicFolder;
            return;
        }

        IsSyncing = true;
        StatusMessage = string.Empty;
        _activeCts = new CancellationTokenSource();

        try
        {
            await _downloadService.SyncSoundCloudLikesAsync(username, authToken, clientId, musicFolder, _activeCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SoundCloud sync failed");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsSyncing = false;
            _activeCts?.Dispose();
            _activeCts = null;
        }
    }

    private async Task<string?> GetPrimaryMusicFolderAsync()
    {
        var downloadFolder = await _settingsService.GetDownloadFolderPathAsync();
        if (!string.IsNullOrEmpty(downloadFolder) && Directory.Exists(downloadFolder))
            return downloadFolder;

        var folders = await _libraryReader.GetAllFoldersAsync();
        return folders.FirstOrDefault()?.Path;
    }
}
