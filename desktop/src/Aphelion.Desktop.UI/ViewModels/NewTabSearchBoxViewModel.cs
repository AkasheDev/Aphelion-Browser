using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Enums;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>Owns cancellable type-ahead state for a single New Tab search box.</summary>
public sealed partial class NewTabSearchBoxViewModel : ViewModelBase, IDisposable
{
    private readonly ISearchSuggestionProvider? _suggestions;
    private readonly SearchEngineSelectorViewModel? _engines;
    private readonly IPrivacyPreferenceStore? _privacy;
    private readonly Func<string, bool> _search;
    private CancellationTokenSource? _request;

    public NewTabSearchBoxViewModel(
        ISearchSuggestionProvider? suggestions,
        SearchEngineSelectorViewModel? engines,
        Func<string, bool> search,
        IPrivacyPreferenceStore? privacy = null)
    {
        _suggestions = suggestions;
        _engines = engines;
        _privacy = privacy;
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _isSuggestionsEnabled = _privacy?.Load().SearchSuggestionsEnabled ?? true;
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

    /// <summary>
    /// Whether the suggestion list should be on screen: there is something to
    /// show, and the search box has not been left.
    /// </summary>
    /// <remarks>
    /// Losing focus hides the list without discarding it, so returning to a box
    /// whose text has not changed shows the same suggestions immediately rather
    /// than waiting out the debounce and another round trip for a query the user
    /// never altered.
    /// </remarks>
    public bool HasSuggestions => Suggestions.Count > 0 && !_isSearchBoxUnfocused;

    private bool _isSearchBoxUnfocused;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// Whether typed text may be sent to the selected search engine's suggestion
    /// endpoint as it is typed. On by default; off clears and suppresses every
    /// future request until turned back on.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestionsStateLabel))]
    private bool _isSuggestionsEnabled;

    public string SuggestionsStateLabel => IsSuggestionsEnabled ? "On" : "Off";

    partial void OnSearchTextChanged(string value) => RequestSuggestions();

    /// <summary>
    /// Called when the search box takes focus. Suggestions kept from before are
    /// shown again as they are; a box whose text arrived while it was unfocused
    /// asks for them instead.
    /// </summary>
    public void RequestSuggestionsForFocus()
    {
        var wasHidden = _isSearchBoxUnfocused;
        _isSearchBoxUnfocused = false;

        if (Suggestions.Count > 0)
        {
            if (wasHidden)
            {
                OnPropertyChanged(nameof(HasSuggestions));
            }

            return;
        }

        RequestSuggestions();
    }

    /// <summary>
    /// Called when the search box loses focus. The list goes off screen but is
    /// kept, so coming back to an unchanged query is instant — see the remarks
    /// on <see cref="HasSuggestions"/>.
    /// </summary>
    public void HideSuggestionsForLostFocus()
    {
        if (_isSearchBoxUnfocused)
        {
            return;
        }

        _isSearchBoxUnfocused = true;
        OnPropertyChanged(nameof(HasSuggestions));
    }

    partial void OnIsSuggestionsEnabledChanged(bool value)
    {
        _privacy?.Save(_privacy.Load() with { SearchSuggestionsEnabled = value });

        if (value)
        {
            RequestSuggestions();
        }
        else
        {
            _request?.Cancel();
            _request?.Dispose();
            _request = null;
            ClearSuggestions();
        }
    }

    [RelayCommand]
    private void ToggleSuggestions() => IsSuggestionsEnabled = !IsSuggestionsEnabled;

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

        if (_suggestions is null || _engines is null || !IsSuggestionsEnabled || SearchText.Trim().Length < 2)
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

    /// <summary>
    /// Called when the suggestions are dismissed outright — the page stopped
    /// being visible, or a click landed away from the search box. Unlike losing
    /// focus, this discards the list rather than parking it. Any in-flight
    /// request is cancelled too, so a reply arriving afterwards cannot
    /// repopulate a list the user has dismissed.
    /// </summary>
    public void ClearSuggestionsForHidden()
    {
        _request?.Cancel();
        _request?.Dispose();
        _request = null;

        // Reset alongside the clear: with nothing kept, leaving the hidden flag
        // set would suppress the next set of results until focus was cycled.
        _isSearchBoxUnfocused = false;
        ClearSuggestions();
    }

    /// <summary>
    /// Empties the list, tolerating a call that arrives while the collection is
    /// already raising a change of its own.
    /// </summary>
    /// <remarks>
    /// Suggestions are cleared from several directions — a keystroke, the popup
    /// closing, the tab being hidden — and some of those fire from inside the
    /// ItemsControl's own handling of a previous change to this same
    /// collection. ObservableCollection refuses a nested mutation and throws
    /// InvalidOperationException ("Cannot change ObservableCollection during a
    /// CollectionChanged event"). Rather than making each caller reason about
    /// whether it is nested, the clear is deferred to the next dispatcher pass
    /// when that turns out to be the case.
    /// </remarks>
    private void ClearSuggestions()
    {
        if (Suggestions.Count == 0)
        {
            return;
        }

        try
        {
            Suggestions.Clear();
        }
        catch (InvalidOperationException)
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (Suggestions.Count > 0)
                    {
                        Suggestions.Clear();
                    }
                },
                DispatcherPriority.Background);
        }
    }

    private void OnSuggestionsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(HasSuggestions));
}
