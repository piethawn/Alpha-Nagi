using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Nagi.WinUI.Models;

namespace Nagi.WinUI.Services.Abstractions;

/// <summary>
///     Manages yt-dlp and scdl download processes, exposing a live queue and triggering
///     library rescans on completion. Lifetime is singleton — downloads survive page navigation.
/// </summary>
public interface IDownloadService
{
    ObservableCollection<DownloadItem> Downloads { get; }

    Task DownloadUrlAsync(string url, string musicFolder, CancellationToken ct = default);

    Task SyncSoundCloudLikesAsync(
        string username,
        string authToken,
        string clientId,
        string musicFolder,
        CancellationToken ct = default);
}
