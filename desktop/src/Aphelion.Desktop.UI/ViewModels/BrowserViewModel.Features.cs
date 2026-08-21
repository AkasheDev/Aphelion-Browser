using System.Collections.ObjectModel;
using System.Text.Json;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain;
using Aphelion.Desktop.Domain.Enums;
using Aphelion.Desktop.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

public sealed partial class BrowserViewModel
{
    public bool IsSettingsPage => _tab.IsSettingsPage;

    public bool IsHistoryPage => _tab.IsHistoryPage;

    public bool HasInternalPage => _tab.HasInternalPage;

    public SettingsViewModel? Settings { get; private set; }

    public HistoryViewModel? History { get; private set; }

    /// <summary>Per-window WebView2 / WKWebView profile directory for private windows.</summary>
    public string? IsolatedUserDataDirectory { get; private set; }

    public string? PreferredDownloadDirectory => Settings?.PreferredDownloadDirectory;

    [ObservableProperty]
    private bool _isFindBarOpen;

    [ObservableProperty]
    private string _findQuery = string.Empty;

    [ObservableProperty]
    private string _findStatus = string.Empty;

    [ObservableProperty]
    private bool _findMatchCase;

    [ObservableProperty]
    private bool _isSiteInfoOpen;

    [ObservableProperty]
    private bool _isPageMenuOpen;

    [ObservableProperty]
    private string? _pageMenuLink;

    [ObservableProperty]
    private string? _pageMenuImage;

    [ObservableProperty]
    private string? _pageMenuSelection;

    [ObservableProperty]
    private bool _isAddressEditing;

    public ObservableCollection<string> AddressSuggestions { get; } = [];

    public bool HasAddressSuggestions => AddressSuggestions.Count > 0 && IsAddressEditing;

    public bool IsSecure => _tab.Address?.IsSecure == true;

    public string SiteHost => _tab.Address?.DisplayHost ?? string.Empty;

    public string SiteScheme => _tab.Address?.Value.Scheme ?? string.Empty;

    public string OmniboxBadge =>
        AddressBarInput.Resolve(AddressText, out _, out _) == AddressBarIntent.Search
            ? "This looks like a search"
            : IsSecure ? "Connection is secure"
            : HasInternalPage || IsDownloadsPage || IsBlank ? "Aphelion page"
            : string.IsNullOrEmpty(AddressText) ? "Site information"
            : "Connection is not secure";

    public void ConfigureChrome(
        SettingsViewModel? settings,
        HistoryViewModel? history,
        string? isolatedUserDataDirectory)
    {
        Settings = settings;
        History = history;
        IsolatedUserDataDirectory = isolatedUserDataDirectory;
        if (_isPrivate && history is not null)
        {
            history.IsPrivateSession = true;
        }
    }

    public bool NavigateFromOmnibox(string query) => NavigateFromNewTab(query);

    public void ShowSettings(bool recordHistory = true) =>
        ShowInternal(InternalPageKind.Settings, recordHistory);

    public void ShowHistory(bool recordHistory = true) =>
        ShowInternal(InternalPageKind.History, recordHistory);

    public void ShowInternal(InternalPageKind kind, bool recordHistory = true)
    {
        if (_tab.InternalPage == kind)
        {
            AddressText = InternalPages.AddressOf(kind);
            return;
        }

        ErrorPage.Hide();
        if (recordHistory)
        {
            PushCurrentToLocalBack();
        }

        _tab.ShowInternal(kind);
        _newTabDepth = NotAnchoredToNewTab;
        _newTabForward = null;
        SyncFromTab();
    }

    [RelayCommand]
    private void OpenFindBar()
    {
        if (_tab.IsLocalSurface)
        {
            return;
        }

        IsFindBarOpen = true;
    }

    [RelayCommand]
    private async Task FindNext()
    {
        if (_session is null || string.IsNullOrEmpty(FindQuery))
        {
            return;
        }

        var result = await _session.FindAsync(FindQuery, forward: true, FindMatchCase).ConfigureAwait(true);
        FindStatus = result is null || result.MatchCount == 0
            ? "No matches"
            : $"{result.MatchCount} matches";
    }

    [RelayCommand]
    private async Task FindPrevious()
    {
        if (_session is null || string.IsNullOrEmpty(FindQuery))
        {
            return;
        }

        var result = await _session.FindAsync(FindQuery, forward: false, FindMatchCase).ConfigureAwait(true);
        FindStatus = result is null || result.MatchCount == 0
            ? "No matches"
            : $"{result.MatchCount} matches";
    }

    [RelayCommand]
    private async Task CloseFindBar()
    {
        IsFindBarOpen = false;
        FindStatus = string.Empty;
        if (_session is not null)
        {
            await _session.StopFindAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task PrintPage()
    {
        if (_session is not null && !_tab.IsLocalSurface)
        {
            await _session.PrintAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void OpenDevTools() => _session?.OpenDevTools();

    [RelayCommand]
    private void ToggleSiteInfo() => IsSiteInfoOpen = !IsSiteInfoOpen;

    [RelayCommand]
    private void CloseSiteInfo() => IsSiteInfoOpen = false;

    [RelayCommand]
    private void ClosePageMenu() => IsPageMenuOpen = false;

    [RelayCommand]
    private void PageMenuBack() => GoBack();

    [RelayCommand]
    private void PageMenuForward() => GoForward();

    [RelayCommand]
    private void PageMenuReload() => Reload();

    public event EventHandler<string>? OpenInNewTabRequested;

    public event EventHandler<string>? SearchRequested;

    public event EventHandler<string>? CopyTextRequested;

    [RelayCommand]
    private void OpenPageMenuLink()
    {
        if (!string.IsNullOrWhiteSpace(PageMenuLink))
        {
            OpenInNewTabRequested?.Invoke(this, PageMenuLink);
        }

        ClosePageMenu();
    }

    [RelayCommand]
    private void SavePageMenuLink()
    {
        if (Uri.TryCreate(PageMenuLink, UriKind.Absolute, out var uri))
        {
            StartLinkDownload(uri, Path.GetFileName(uri.LocalPath));
        }

        ClosePageMenu();
    }

    [RelayCommand]
    private void CopyPageMenuSelection()
    {
        var text = !string.IsNullOrWhiteSpace(PageMenuSelection) ? PageMenuSelection : PageMenuLink;
        if (!string.IsNullOrWhiteSpace(text))
        {
            CopyTextRequested?.Invoke(this, text);
        }

        ClosePageMenu();
    }

    [RelayCommand]
    private void SearchPageMenuSelection()
    {
        if (!string.IsNullOrWhiteSpace(PageMenuSelection))
        {
            SearchRequested?.Invoke(this, PageMenuSelection);
        }

        ClosePageMenu();
    }

    public async Task ApplyMuteAsync()
    {
        if (_session is not null)
        {
            await _session.SetMutedAsync(_tab.IsMuted).ConfigureAwait(true);
        }
    }

    public IEngineDownloadOperation? StartLinkDownload(Uri source, string fileName)
    {
        if (_session is null)
        {
            return null;
        }

        var folder = PreferredDownloadDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(folder);
        var safe = string.IsNullOrWhiteSpace(fileName) ? "download" : fileName;
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(c, '_');
        }

        var path = Path.Combine(folder, safe);
        if (File.Exists(path))
        {
            path = Path.Combine(
                folder,
                $"{Path.GetFileNameWithoutExtension(safe)}-{Guid.NewGuid():N}{Path.GetExtension(safe)}");
        }

        var operation = _session.StartDownload(source, path);
        _downloads?.Track(operation, _isPrivate);
        DownloadStarted?.Invoke(this, EventArgs.Empty);
        return operation;
    }

    internal void HandlePageMessage(EnginePageMessageEventArgs e)
    {
        switch (e.Kind)
        {
            case "fs":
                ContainsFullScreenElement = PayloadFlag(e.Payload, "on");
                break;
            case "ctx":
                PageMenuLink = PayloadString(e.Payload, "href");
                PageMenuImage = PayloadString(e.Payload, "src");
                PageMenuSelection = PayloadString(e.Payload, "sel");
                IsPageMenuOpen = true;
                break;
            case "download":
                var href = PayloadString(e.Payload, "href");
                if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
                {
                    StartLinkDownload(uri, PayloadString(e.Payload, "name") ?? "download");
                }

                break;
        }
    }

    internal void RecordHistoryIfNeeded()
    {
        if (_isPrivate || History is null || _tab.Address is null)
        {
            return;
        }

        History.Record(new HistoryVisit(
            DateTimeOffset.Now,
            _tab.Address.ToString(),
            _tab.DisplayTitle,
            _tab.SearchTerm));
    }

    internal void RefreshOmnibox()
    {
        OnPropertyChanged(nameof(IsSecure));
        OnPropertyChanged(nameof(SiteHost));
        OnPropertyChanged(nameof(SiteScheme));
        OnPropertyChanged(nameof(OmniboxBadge));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(IsHistoryPage));
        OnPropertyChanged(nameof(HasInternalPage));
        OnPropertyChanged(nameof(ShouldShowWebView));

        // The page stops regrouping itself while nothing is showing it, so a tab
        // arriving on the history has to ask for what it missed.
        if (IsHistoryPage)
        {
            History?.RefreshIfStale();
        }
    }

    /// <summary>
    /// Starts an editing session in the address bar.
    /// </summary>
    /// <remarks>
    /// The privacy preference is read here rather than per keystroke: the store
    /// reads the file on every call, and the answer cannot change in the middle
    /// of someone typing a single address.
    /// </remarks>
    public void BeginAddressEditing()
    {
        IsAddressEditing = true;
        _suggestionsAllowed = _privacy?.Load().SearchSuggestionsEnabled ?? true;
        RequestAddressSuggestions();
    }

    /// <summary>
    /// Asks for suggestions matching what is in the box now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be called once, when the address bar took focus, and never
    /// again. Focus is also where the existing address gets selected, so the
    /// suggestions on offer were completions of the address the user was in the
    /// middle of replacing, and they never caught up with what was typed.
    /// </para>
    /// <para>
    /// The privacy toggle was not consulted at all on this path. Turning
    /// suggestions off stopped the New Tab box and left the address bar sending
    /// every focused address to the search engine, which is the one thing the
    /// setting promises it will not do.
    /// </para>
    /// </remarks>
    private void RequestAddressSuggestions()
    {
        _suggestionRequest?.Cancel();
        _suggestionRequest?.Dispose();
        _suggestionRequest = null;

        var query = AddressText?.Trim();

        if (!_suggestionsAllowed ||
            _suggestions is null ||
            _searchEngines is null ||
            string.IsNullOrEmpty(query) ||
            query.Length < 2)
        {
            ClearAddressSuggestions();
            return;
        }

        _suggestionRequest = new CancellationTokenSource();
        _ = LoadAddressSuggestionsAsync(query, _searchEngines.Selected.Kind, _suggestionRequest.Token);
    }

    private async Task LoadAddressSuggestionsAsync(
        string query,
        SearchEngineKind engine,
        CancellationToken cancellationToken)
    {
        try
        {
            // Let the typing settle before spending a request on it, the same
            // beat the New Tab box waits.
            await Task.Delay(TimeSpan.FromMilliseconds(220), cancellationToken).ConfigureAwait(true);

            var list = await _suggestions!
                .GetSuggestionsAsync(engine, query, cancellationToken)
                .ConfigureAwait(true);

            // A reply that arrived after the box moved on is not an answer to
            // what is in it now.
            if (cancellationToken.IsCancellationRequested ||
                !string.Equals(query, AddressText?.Trim(), StringComparison.Ordinal) ||
                _searchEngines?.Selected.Kind != engine ||
                !IsAddressEditing)
            {
                return;
            }

            AddressSuggestions.Clear();
            foreach (var item in list.Take(6))
            {
                AddressSuggestions.Add(item);
            }

            OnPropertyChanged(nameof(HasAddressSuggestions));
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later keystroke.
        }
    }

    private void ClearAddressSuggestions()
    {
        if (AddressSuggestions.Count > 0)
        {
            AddressSuggestions.Clear();
        }

        OnPropertyChanged(nameof(HasAddressSuggestions));
    }

    /// <summary>
    /// Typing is the only thing that should move the suggestions. The address
    /// text is also written by navigation, by opening an internal page and by
    /// choosing a suggestion, and none of those is a question for the search
    /// engine - the editing flag is what tells them apart.
    /// </summary>
    partial void OnAddressTextChanged(string value)
    {
        if (IsAddressEditing)
        {
            RequestAddressSuggestions();
        }
    }

    [RelayCommand]
    private void ChooseAddressSuggestion(string? suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            return;
        }

        AddressText = suggestion;
        Navigate();

        // Nothing here touches the list. Emptying it from inside a click on one
        // of its own rows was tried three ways and crashed three ways: as a
        // reset, as a reset deferred to the next dispatcher pass, and as indexed
        // removes, that last one out of bounds because the panel had already
        // dropped its containers by the time the click was being handled. The
        // popup is bound to this flag as well as to the count, so dropping the
        // flag is enough to close it, and the list is emptied by the refresh that
        // runs whenever the address bar takes focus - which is where clearing it
        // has always been safe.
        IsAddressEditing = false;
        OnPropertyChanged(nameof(HasAddressSuggestions));
    }

    private static bool PayloadFlag(string? json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "{}");
            return doc.RootElement.TryGetProperty(name, out var value) &&
                   value.ValueKind is JsonValueKind.True;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? PayloadString(string? json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "{}");
            return doc.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
