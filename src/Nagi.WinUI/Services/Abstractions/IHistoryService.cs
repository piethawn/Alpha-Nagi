using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Nagi.WinUI.Models;

namespace Nagi.WinUI.Services.Abstractions;

/// <summary>
///     Reads from and manages the existing ListenHistory table to provide a play history
///     display. History is session-persistent and survives app restarts.
/// </summary>
public interface IHistoryService
{
    ObservableCollection<PlayHistoryEntry> History { get; }

    Task LoadHistoryAsync();

    Task ClearHistoryAsync();
}
