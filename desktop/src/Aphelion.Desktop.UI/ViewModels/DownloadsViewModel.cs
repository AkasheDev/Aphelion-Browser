using System.Collections.ObjectModel;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.Enums;
using Aphelion.Desktop.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// The download list behind the toolbar's downloads button: live transfers
/// first-class, finished ones as history, restored across runs.
/// </summary>
/// <remarks>
/// One per profile rather than per window, like bookmarks: a download started
/// in one window is still this user's download in every other, and Chrome's
/// bubble behaves the same way. Which window has the panel open is each
/// shell's own state; only the list itself is shared.
/// </remarks>
public sealed partial class DownloadsViewModel : ViewModelBase
{
    /// <summary>
    /// How much history survives. Enough that nothing recent is ever missed,
    /// small enough that the panel and its JSON file stay instant.
    /// </summary>
    private const int HistoryLimit = 100;

    private readonly IDownloadHistoryStore? _store;
    private readonly IFileExplorer? _files;

    public DownloadsViewModel(IDownloadHistoryStore? store = null, IFileExplorer? files = null)
    {
        _store = store;
        _files = files;
        Items.CollectionChanged += (_, _) => RefreshVisible();
        RestoreHistory();
        RefreshVisible();
    }

    /// <summary>Every tracked download, newest first.</summary>
    public ObservableCollection<DownloadItemViewModel> Items { get; } = [];

    /// <summary>What the page lists after the search box has filtered it.</summary>
    public ObservableCollection<DownloadItemViewModel> VisibleItems { get; } = [];

    public bool HasDownloads => Items.Count > 0;

    public bool HasVisibleDownloads => VisibleItems.Count > 0;

    /// <summary>Search ran against a non-empty list and matched nothing.</summary>
    public bool HasNoSearchMatches => HasDownloads && !HasVisibleDownloads;

    /// <summary>Filters the list by file name, as chrome://downloads does.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => RefreshVisible();

    /// <summary>True while any transfer is running or paused — lights the ring.</summary>
    [ObservableProperty]
    private bool _hasActiveDownloads;

    /// <summary>
    /// Combined progress of the live transfers as a 0–360 sweep for the
    /// toolbar ring. Transfers with unknown size count as no progress, so the
    /// ring underestimates rather than lies.
    /// </summary>
    [ObservableProperty]
    private double _activeSweepAngle;

    /// <summary>
    /// Starts tracking a transfer the engine has begun. A private window's
    /// download is shown — the user must be able to pause or cancel it — but
    /// never written to the history file.
    /// </summary>
    public void Track(IEngineDownloadOperation operation, bool isPrivate = false)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var source = operation.Source ?? new Uri("about:blank");
        var filePath = string.IsNullOrWhiteSpace(operation.FilePath)
            ? Path.Combine(_files?.DownloadsDirectory ?? string.Empty, "download")
            : operation.FilePath;

        var item = DownloadItem.Start(source, filePath, operation.TotalBytes);
        var row = new DownloadItemViewModel(
            item,
            operation,
            _files,
            OnRowStateChanged,
            OnRowProgressed,
            Remove,
            isPrivate);

        Items.Insert(0, row);
        TrimHistory();
        RefreshVisible();
        RefreshActive();
        Persist();
    }

    /// <summary>Removes one row. A live transfer is canceled first — a row the
    /// user threw away must not keep writing to disk invisibly.</summary>
    public void Remove(DownloadItemViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.CancelIfLive();

        if (Items.Remove(row))
        {
            RefreshVisible();
            RefreshActive();
            Persist();
        }
    }

    /// <summary>
    /// Clears the finished entries. Live transfers stay, as in Chrome: clearing
    /// a list is housekeeping, not a way to silently abort work in progress.
    /// </summary>
    [RelayCommand]
    private void ClearFinished()
    {
        var removed = false;

        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!Items[i].IsLive)
            {
                Items.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
        {
            RefreshVisible();
            RefreshActive();
            Persist();
        }
    }

    [RelayCommand]
    private void OpenDownloadsFolder()
    {
        if (_files is null)
        {
            return;
        }

        // The most recent finished file marks the folder downloads actually
        // land in; the profile default covers an empty history.
        var recent = Items.FirstOrDefault(row =>
            row.Item.State == DownloadState.Completed &&
            File.Exists(row.FilePath));

        var folder = recent is not null
            ? Path.GetDirectoryName(recent.FilePath)
            : null;

        _files.OpenFolder(folder ?? _files.DownloadsDirectory);
    }

    private void OnRowStateChanged(DownloadItemViewModel _)
    {
        RefreshActive();
        Persist();
    }

    private void OnRowProgressed(DownloadItemViewModel _) => RefreshActive();

    private void RefreshActive()
    {
        long received = 0;
        long total = 0;
        var anyLive = false;

        foreach (var row in Items)
        {
            if (!row.IsLive)
            {
                continue;
            }

            anyLive = true;

            if (row.Item.TotalBytes is { } rowTotal)
            {
                received += Math.Min(row.Item.ReceivedBytes, rowTotal);
                total += rowTotal;
            }
        }

        HasActiveDownloads = anyLive;
        ActiveSweepAngle = anyLive && total > 0
            ? Math.Clamp(360d * received / total, 0d, 360d)
            : 0d;
    }

    private void TrimHistory()
    {
        for (var i = Items.Count - 1; i >= 0 && Items.Count > HistoryLimit; i--)
        {
            // Only finished rows age out; a live transfer is never displaced,
            // however old the history around it.
            if (!Items[i].IsLive)
            {
                Items.RemoveAt(i);
            }
        }
    }

    private void RestoreHistory()
    {
        if (_store is null)
        {
            return;
        }

        foreach (var record in _store.Load().Take(HistoryLimit))
        {
            if (!Uri.TryCreate(record.Url, UriKind.Absolute, out var source) ||
                string.IsNullOrWhiteSpace(record.FilePath))
            {
                continue;
            }

            var id = DownloadId.TryParse(record.Id, out var parsed) ? parsed : DownloadId.New();
            var state = Enum.TryParse<DownloadState>(record.State, out var parsedState) &&
                Enum.IsDefined(parsedState)
                    ? parsedState
                    : DownloadState.Failed;

            var item = DownloadItem.Restore(
                id,
                source,
                record.FilePath,
                record.TotalBytes,
                record.ReceivedBytes,
                state,
                record.StartedAt,
                record.EndedAt);

            Items.Add(new DownloadItemViewModel(
                item,
                operation: null,
                _files,
                OnRowStateChanged,
                OnRowProgressed,
                Remove));
        }

        RefreshVisible();
    }

    private void RefreshVisible()
    {
        var query = SearchText.Trim();
        VisibleItems.Clear();

        foreach (var row in Items)
        {
            if (query.Length == 0 ||
                row.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.SourceHost.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                VisibleItems.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasDownloads));
        OnPropertyChanged(nameof(HasVisibleDownloads));
        OnPropertyChanged(nameof(HasNoSearchMatches));
    }

    private void Persist()
    {
        if (_store is null)
        {
            return;
        }

        var records = new List<DownloadRecord>(Items.Count);

        foreach (var row in Items)
        {
            if (row.IsPrivate)
            {
                continue;
            }

            var item = row.Item;
            records.Add(new DownloadRecord(
                item.Id.ToString(),
                item.Source.ToString(),
                item.FilePath,
                item.TotalBytes,
                item.ReceivedBytes,
                item.State.ToString(),
                item.StartedAt,
                item.EndedAt));
        }

        _store.Save(records);
    }
}
