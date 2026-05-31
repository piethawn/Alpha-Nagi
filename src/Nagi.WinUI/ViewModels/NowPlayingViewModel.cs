using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;
using Nagi.WinUI.Services.Abstractions;

namespace Nagi.WinUI.ViewModels;

/// <summary>
///     Powers the Now Playing page. Delegates lyrics state to <see cref="LyricsPageViewModel" />
///     and queue state to <see cref="PlayerViewModel" />. All playback commands are forwarded
///     to the existing <see cref="PlayerViewModel" /> — no state is duplicated.
/// </summary>
public partial class NowPlayingViewModel : ObservableObject, IDisposable
{
    private readonly IMusicPlaybackService _playbackService;
    private readonly ILibraryReader _libraryReader;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILogger<NowPlayingViewModel> _logger;
    private bool _isDisposed;

    public NowPlayingViewModel(
        PlayerViewModel playerViewModel,
        LyricsPageViewModel lyricsViewModel,
        IMusicPlaybackService playbackService,
        ILibraryReader libraryReader,
        IDispatcherService dispatcherService,
        ILogger<NowPlayingViewModel> logger)
    {
        PlayerViewModel = playerViewModel;
        LyricsViewModel = lyricsViewModel;
        _playbackService = playbackService;
        _libraryReader = libraryReader;
        _dispatcherService = dispatcherService;
        _logger = logger;

        _playbackService.TrackChanged += OnTrackChanged;
        _playbackService.QueueChanged += OnQueueChanged;

        RefreshQueueItems();
    }

    /// <summary>The global player ViewModel — binds controls without duplicating state.</summary>
    public PlayerViewModel PlayerViewModel { get; }

    /// <summary>Dedicated lyrics ViewModel instance for this page.</summary>
    public LyricsPageViewModel LyricsViewModel { get; }

    /// <summary>Projected queue items (Song objects) for display in the Up Next list.</summary>
    public ObservableCollection<Song> QueueItems { get; } = new();

    public Song? CurrentTrack => _playbackService.CurrentTrack;

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _playbackService.TrackChanged -= OnTrackChanged;
        _playbackService.QueueChanged -= OnQueueChanged;

        LyricsViewModel.Dispose();
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    private async Task JumpToQueueItemAsync(Song? song)
    {
        if (song == null) return;

        var queue = _playbackService.IsShuffleEnabled
            ? _playbackService.ShuffledQueue
            : _playbackService.PlaybackQueue;

        var idx = -1;
        for (var i = 0; i < queue.Count; i++)
        {
            if (queue[i] == song.Id) { idx = i; break; }
        }

        if (idx >= 0)
            await _playbackService.PlayQueueItemAsync(idx);
    }

    private void OnTrackChanged()
    {
        _dispatcherService.TryEnqueue(() =>
        {
            if (_isDisposed) return;
            OnPropertyChanged(nameof(CurrentTrack));
            RefreshQueueItems();
        });
    }

    private void OnQueueChanged()
    {
        _dispatcherService.TryEnqueue(() =>
        {
            if (_isDisposed) return;
            RefreshQueueItems();
        });
    }

    private async void RefreshQueueItems()
    {
        try
        {
            var queue = _playbackService.IsShuffleEnabled
                ? _playbackService.ShuffledQueue
                : _playbackService.PlaybackQueue;

            var currentId = _playbackService.CurrentTrack?.Id;
            var startIdx = 0;
            if (currentId.HasValue)
            {
                for (var i = 0; i < queue.Count; i++)
                    if (queue[i] == currentId.Value) { startIdx = i; break; }
            }

            QueueItems.Clear();

            var limit = Math.Min(queue.Count, startIdx + 50);
            for (var i = Math.Max(0, startIdx); i < limit; i++)
            {
                var song = await _libraryReader.GetSongByIdAsync(queue[i]).ConfigureAwait(false);
                if (song != null)
                {
                    _dispatcherService.TryEnqueue(() => { if (!_isDisposed) QueueItems.Add(song); });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error refreshing Now Playing queue items.");
        }
    }
}
