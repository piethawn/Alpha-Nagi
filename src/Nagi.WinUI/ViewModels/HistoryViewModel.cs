using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nagi.Core.Services.Abstractions;
using Nagi.WinUI.Models;
using Nagi.WinUI.Services.Abstractions;

namespace Nagi.WinUI.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryService _historyService;
    private readonly IMusicPlaybackService _playbackService;
    private readonly ILogger<HistoryViewModel> _logger;

    public HistoryViewModel(
        IHistoryService historyService,
        IMusicPlaybackService playbackService,
        ILogger<HistoryViewModel> logger)
    {
        _historyService = historyService;
        _playbackService = playbackService;
        _logger = logger;
    }

    [ObservableProperty] public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedEntry))]
    public partial PlayHistoryEntry? SelectedEntry { get; set; }

    public bool HasSelectedEntry => SelectedEntry != null;

    public ObservableCollection<PlayHistoryEntry> History => _historyService.History;

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            await _historyService.LoadHistoryAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PlayEntryAsync(PlayHistoryEntry? entry)
    {
        if (entry == null) return;

        try
        {
            await _playbackService.PlayAsync(entry.SongId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to play history entry {SongId}", entry.SongId);
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        await _historyService.ClearHistoryAsync();
    }

}
