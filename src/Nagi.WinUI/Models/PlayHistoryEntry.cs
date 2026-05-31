using System;
using Nagi.WinUI.Resources;

namespace Nagi.WinUI.Models;

/// <summary>
///     A display-ready projection of a ListenHistory row joined with Song metadata.
/// </summary>
public sealed class PlayHistoryEntry
{
    public long Id { get; init; }
    public Guid SongId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ArtistName { get; init; } = string.Empty;
    public string? AlbumArtUri { get; init; }
    public DateTime PlayedAtUtc { get; init; }
    public TimeSpan PlayDuration { get; init; }

    public string RelativeTime => FormatRelativeTime(PlayedAtUtc);

    private static string FormatRelativeTime(DateTime utc)
    {
        var elapsed = DateTime.UtcNow - utc;
        if (elapsed.TotalSeconds < 60) return Resources.Strings.History_Time_JustNow;
        if (elapsed.TotalHours < 1)
            return string.Format(Resources.Strings.History_Time_MinutesAgo, (int)elapsed.TotalMinutes);
        if (elapsed.TotalDays < 1)
            return string.Format(Resources.Strings.History_Time_HoursAgo, (int)elapsed.TotalHours);
        if (elapsed.TotalDays < 2) return Resources.Strings.History_Time_Yesterday;
        return utc.ToLocalTime().ToString("d");
    }
}
