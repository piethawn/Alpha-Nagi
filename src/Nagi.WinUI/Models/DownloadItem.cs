using CommunityToolkit.Mvvm.ComponentModel;

namespace Nagi.WinUI.Models;

public enum DownloadSource { YouTube, SoundCloud }
public enum DownloadStatus { Pending, Downloading, Complete, Failed }

/// <summary>
///     Represents a single entry in the live download queue.
/// </summary>
public partial class DownloadItem : ObservableObject
{
    public DownloadItem(string id, string title, DownloadSource source)
    {
        Id = id;
        Title = title;
        Source = source;
    }

    public string Id { get; }
    public DownloadSource Source { get; }

    [ObservableProperty] public partial string Title { get; set; }
    [ObservableProperty] public partial DownloadStatus Status { get; set; } = DownloadStatus.Pending;
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
}
