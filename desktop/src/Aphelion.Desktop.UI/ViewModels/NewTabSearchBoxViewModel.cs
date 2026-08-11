using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>Owns cancellable type-ahead state for a single New Tab search box.</summary>
public sealed partial class NewTabSearchBoxViewModel : ViewModelBase, IDisposable
{
    private readonly ISearchSuggestionProvider? _suggestions;
    private readonly SearchEngineSelectorViewModel? _engines;
    private readonly Func<string, bool> _search;
    private CancellationTokenSource? _request;

    public NewTabSearchBoxViewModel(
        ISearchSuggestionProvider? suggestions,
        SearchEngineSelectorViewModel? engines,
        Func<string, bool> search)
    {
        _suggestions = suggestions;
        _engines = engines;
        _search = search ?? throw new ArgumentNullException(nameof(search));
        Suggestions.CollectionChanged += OnSuggestionsChanged;

        if (_engines is not null)
        {
            _engines.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SearchEngineSelectorViewModel.Selected))
                {
                    RequestSuggestions();
                }
            };
        }
    }

    public ObservableCollection<string> Suggestions { get; } = [];

    public bool HasSuggestions => Suggestions.Count > 0;

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => RequestSuggestions();

    [RelayCommand]
    private void Search()
    {
        if (_search(SearchText))
        {
            SearchText = string.Empty;
            ClearSuggestions();
        }
    }

    [RelayCommand]
    private void UseSuggestion(string? suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            return;
        }

        SearchText = suggestion;
        Search();
    }

    public void Dispose()
    {
        _request?.Cancel();
        _request?.Dispose();
        _request = null;
        Suggestions.CollectionChanged -= OnSuggestionsChanged;
        GC.SuppressFinalize(this);
    }

    private void RequestSuggestions()
    {
        _request?.Cancel();
        _request?.Dispose();
        _request = null;

        if (_suggestions is null || _engines is null || SearchText.Trim().Length < 2)
        {
            ClearSuggestions();
            return;
        }

        _request = new CancellationTokenSource();
        _ = RefreshSuggestionsAsync(SearchText.Trim(), _engines.Selected.Kind, _request.Token);
    }

    private async Task RefreshSuggestionsAsync(
        string query,
        SearchEngineKind engine,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(220), cancellationToken).ConfigureAwait(true);
            var values = await _suggestions!
                .GetSuggestionsAsync(engine, query, cancellationToken)
                .ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested ||
                !string.Equals(query, SearchText.Trim(), StringComparison.Ordinal) ||
                _engines?.Selected.Kind != engine)
            {
                return;
            }

            Suggestions.Clear();

            foreach (var value in values)
            {
                Suggestions.Add(value);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded keystrokes are expected.
        }
    }

    private void ClearSuggestions() => Suggestions.Clear();

    private void OnSuggestionsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(HasSuggestions));
}
