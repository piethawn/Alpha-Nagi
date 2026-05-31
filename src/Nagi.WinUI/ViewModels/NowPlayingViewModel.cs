using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
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
    private CancellationTokenSource? _refreshCts;
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

    /// <summary>Projected queue items for display in the Up Next list. Carries the exact queue index so playback always targets the right slot.</summary>
    public ObservableCollection<QueueEntry> QueueItems { get; } = new();

    public Song? CurrentTrack => _playbackService.CurrentTrack;

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _playbackService.TrackChanged -= OnTrackChanged;
        _playbackService.QueueChanged -= OnQueueChanged;

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;

        LyricsViewModel.Dispose();
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    private async Task JumpToQueueItemAsync(QueueEntry? entry)
    {
        if (entry == null) return;
        await _playbackService.PlayQueueItemAsync(entry.QueueIndex);
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
        // Cancel any in-flight refresh so stale items from a previous call can't
        // race with this one and produce an interleaved / mis-indexed collection.
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        var ct = cts.Token;

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

            // Fetch all songs first, then update the collection in one atomic swap.
            // This prevents two concurrent refreshes from interleaving their items.
            var limit = Math.Min(queue.Count, startIdx + 50);
            var entries = new List<QueueEntry>(limit - startIdx);

            for (var i = Math.Max(0, startIdx); i < limit; i++)
            {
                if (ct.IsCancellationRequested) return;
                var song = await _libraryReader.GetSongByIdAsync(queue[i]).ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;
                if (song != null)
                    entries.Add(new QueueEntry(i, song));
            }

            _dispatcherService.TryEnqueue(() =>
            {
                if (_isDisposed || ct.IsCancellationRequested) return;
                QueueItems.Clear();
                foreach (var e in entries)
                    QueueItems.Add(e);
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error refreshing Now Playing queue items.");
        }
    }
}

/// <summary>Pairs a song with its exact index in the playback queue so clicks always target the right slot.</summary>
public record QueueEntry(int QueueIndex, Song Song);
