using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nagi.Core.Data;
using Nagi.WinUI.Models;
using Nagi.WinUI.Services.Abstractions;

namespace Nagi.WinUI.Services.Implementations;

/// <summary>
///     Reads play history from the existing ListenHistory table (joined with Song).
///     History entries with a play duration under 5 seconds are excluded (accidental skips).
/// </summary>
public sealed class HistoryService : IHistoryService
{
    private const int MaxHistoryEntries = 100;
    private static readonly TimeSpan MinPlayDuration = TimeSpan.FromSeconds(5);

    private readonly IDbContextFactory<MusicDbContext> _contextFactory;
    private readonly ILogger<HistoryService> _logger;

    public HistoryService(
        IDbContextFactory<MusicDbContext> contextFactory,
        ILogger<HistoryService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public ObservableCollection<PlayHistoryEntry> History { get; } = new();

    public async Task LoadHistoryAsync()
    {
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

            var entries = await ctx.ListenHistory
                .Where(h => h.ListenDurationTicks >= MinPlayDuration.Ticks)
                .OrderByDescending(h => h.ListenTimestampUtc)
                .Take(MaxHistoryEntries)
                .Select(h => new PlayHistoryEntry
                {
                    Id = h.Id,
                    SongId = h.SongId,
                    Title = h.Song.Title ?? string.Empty,
                    ArtistName = h.Song.ArtistName ?? string.Empty,
                    AlbumArtUri = h.Song.AlbumArtUriFromTrack,
                    PlayedAtUtc = h.ListenTimestampUtc,
                    PlayDuration = TimeSpan.FromTicks(h.ListenDurationTicks)
                })
                .ToListAsync()
                .ConfigureAwait(false);

            History.Clear();
            foreach (var e in entries) History.Add(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load play history.");
        }
    }

    public async Task ClearHistoryAsync()
    {
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
            await ctx.ListenHistory.ExecuteDeleteAsync().ConfigureAwait(false);
            History.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear play history.");
        }
    }
}
