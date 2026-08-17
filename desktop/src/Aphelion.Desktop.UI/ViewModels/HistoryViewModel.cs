using System.Collections.ObjectModel;
using System.Globalization;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>The <c>aphelion://history</c> page.</summary>
public sealed partial class HistoryViewModel : ViewModelBase
{
    private readonly IHistoryStore _store;
    private IReadOnlyList<HistoryVisit> _all = [];

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

    partial void OnSearchTextChanged(string value) => Rebuild();

    public void Record(HistoryVisit visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
        _store.Add(visit);
        Reload();
    }

    public void Reload()
    {
        _all = _store.Load();
        Rebuild();
    }

    [RelayCommand]
    private void Delete(HistoryVisit? visit)
    {
        if (visit is null)
        {
            return;
        }

        _store.Delete(visit);
        Reload();
    }

    [RelayCommand]
    private void Clear()
    {
        _store.Clear();
        Reload();
    }

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
                day.Key.ToString("D", CultureInfo.CurrentCulture),
                day.ToList()));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoSearchMatches));
        OnPropertyChanged(nameof(HasDays));
    }
}

public sealed class HistoryDayGroupViewModel(string heading, IReadOnlyList<HistoryVisit> visits)
{
    public string Heading { get; } = heading;

    public IReadOnlyList<HistoryVisit> Visits { get; } = visits;
}
