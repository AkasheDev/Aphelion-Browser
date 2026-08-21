using System.Collections.ObjectModel;
using System.Globalization;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>The <c>aphelion://history</c> page.</summary>
public sealed partial class HistoryViewModel : ViewModelBase
{
    private readonly IHistoryStore _store;
    private readonly List<ShellViewModel> _shells = [];
    private IReadOnlyList<HistoryVisit> _all = [];
    private bool _stale;

    public HistoryViewModel(IHistoryStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Reload();
    }

    public ObservableCollection<HistoryDayGroupViewModel> Days { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isPrivateSession;

    public bool IsEmpty => Days.Count == 0 && string.IsNullOrWhiteSpace(SearchText) && !IsPrivateSession;

    public bool HasNoSearchMatches => Days.Count == 0 && !string.IsNullOrWhiteSpace(SearchText) && !IsPrivateSession;

    public bool HasDays => Days.Count > 0 && !IsPrivateSession;

    /// <summary>Everything recorded, not just what the current search shows.</summary>
    public string TotalLabel => _all.Count switch
    {
        0 => "Nothing recorded",
        1 => "1 page",
        var count => $"{count:N0} pages",
    };

    /// <summary>The stat beside it: how much of that total is from today.</summary>
    public string TodayLabel
    {
        get
        {
            var today = _all.Count(visit => visit.VisitedAt.LocalDateTime.Date == DateTime.Today);
            return today == 1 ? "1 today" : $"{today:N0} today";
        }
    }

    /// <summary>How many rows the current search is showing, for the filter chip.</summary>
    public string MatchLabel
    {
        get
        {
            var matches = Days.Sum(day => day.Visits.Count);
            return matches == 1 ? "1 match" : $"{matches:N0} matches";
        }
    }

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    partial void OnSearchTextChanged(string value) => Rebuild();

    partial void OnIsPrivateSessionChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoSearchMatches));
        OnPropertyChanged(nameof(HasDays));
    }

    /// <summary>
    /// Records a visit. The page is only regrouped when one is actually on
    /// screen: a rebuild walks every kept visit and replaces the whole day tree,
    /// which is wasted on the ordinary case of browsing with the history page
    /// nowhere in sight. Otherwise the work is left for whoever opens it next.
    /// </summary>
    public void Record(HistoryVisit visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
        _store.Add(visit);

        if (IsOnScreen())
        {
            Reload();
            return;
        }

        _stale = true;
    }

    /// <summary>
    /// Whether any open window is showing this page. Windows already register
    /// here so a click can be served by whichever one is in front, so the answer
    /// costs a walk of the open windows rather than a subscription the tabs
    /// would have to remember to undo - a tab is dropped when it closes, with no
    /// teardown to hang one off.
    /// </summary>
    private bool IsOnScreen() =>
        _shells.Any(shell =>
            shell.ActiveBrowser?.IsHistoryPage == true ||
            shell.SplitBrowser?.IsHistoryPage == true);

    /// <summary>
    /// Brings the page up to date if it fell behind while nothing was showing
    /// it. Called as a tab settles on the history page.
    /// </summary>
    public void RefreshIfStale()
    {
        if (_stale)
        {
            Reload();
        }
    }

    public void Reload()
    {
        _stale = false;
        _all = _store.Load();
        Rebuild();
    }

    /// <summary>
    /// The page is one per profile and every window draws the same instance, so
    /// it cannot hold a callback pointing at a single shell — the last window
    /// opened would answer for all of them. Windows register instead, and a
    /// click is served by whichever one is in front, as bookmarks do.
    /// </summary>
    public void Register(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        if (!_shells.Contains(shell))
        {
            _shells.Add(shell);
        }
    }

    public void Unregister(ShellViewModel shell) => _shells.Remove(shell);

    private ShellViewModel? ActiveShell =>
        _shells.FirstOrDefault(shell => shell.IsWindowActive) ?? _shells.FirstOrDefault();

    [RelayCommand]
    private void Open(HistoryVisitViewModel? row)
    {
        if (row is null ||
            !Uri.TryCreate(row.Address, UriKind.Absolute, out var uri) ||
            !PageAddress.TryCreate(uri, out var address) ||
            address is null)
        {
            return;
        }

        ActiveShell?.NavigateActiveTab(address);
    }

    [RelayCommand]
    private void Delete(HistoryVisitViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        _store.Delete(row.Visit);
        Reload();
    }

    [RelayCommand]
    private void Clear()
    {
        _store.Clear();
        Reload();
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    private void Rebuild()
    {
        Days.Clear();
        var query = SearchText.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? _all
            : _all.Where(visit =>
                visit.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                visit.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (visit.SearchTerm?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        foreach (var day in matches.GroupBy(visit => visit.VisitedAt.LocalDateTime.Date))
        {
            Days.Add(new HistoryDayGroupViewModel(
                HeadingFor(day.Key),
                day.Select(visit => new HistoryVisitViewModel(visit)).ToList()));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoSearchMatches));
        OnPropertyChanged(nameof(HasDays));
        OnPropertyChanged(nameof(TotalLabel));
        OnPropertyChanged(nameof(TodayLabel));
        OnPropertyChanged(nameof(MatchLabel));
        OnPropertyChanged(nameof(IsSearching));
    }

    /// <summary>Chrome's wording: the two recent days are named, the rest dated.</summary>
    private static string HeadingFor(DateTime day)
    {
        if (day == DateTime.Today)
        {
            return "Today";
        }

        return day == DateTime.Today.AddDays(-1)
            ? "Yesterday"
            : day.ToString("D", CultureInfo.CurrentCulture);
    }
}

public sealed class HistoryDayGroupViewModel(string heading, IReadOnlyList<HistoryVisitViewModel> visits)
{
    public string Heading { get; } = heading;

    public IReadOnlyList<HistoryVisitViewModel> Visits { get; } = visits;

    public string CountLabel => Visits.Count == 1 ? "1 page" : $"{Visits.Count} pages";
}

/// <summary>One row on the timeline.</summary>
public sealed class HistoryVisitViewModel(HistoryVisit visit)
{
    public HistoryVisit Visit { get; } = visit;

    public string Address => Visit.Address;

    /// <summary>Pages saved without a title still need something to click.</summary>
    public string Title => string.IsNullOrWhiteSpace(Visit.Title) ? Host : Visit.Title;

    public string Host =>
        Uri.TryCreate(Visit.Address, UriKind.Absolute, out var uri)
            ? uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host
            : Visit.Address;

    public string TimeLabel => Visit.VisitedAt.LocalDateTime.ToString("HH:mm", CultureInfo.CurrentCulture);

    public bool IsSearch => !string.IsNullOrWhiteSpace(Visit.SearchTerm);

    public string SearchTerm => Visit.SearchTerm ?? string.Empty;
}
