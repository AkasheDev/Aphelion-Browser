using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Application.UseCases;
using Aphelion.Desktop.Domain;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// Drives the browser chrome: address bar, navigation buttons and status.
/// </summary>
/// <remarks>
/// Holds no browsing rules of its own. Deciding whether typed text is an address
/// or a search belongs to the application layer, and the tab's state transitions
/// belong to the domain; this class only reflects them for the view.
/// </remarks>
public sealed partial class BrowserViewModel : ViewModelBase
{
    private readonly NavigateFromAddressBar _navigateFromAddressBar;
    private readonly ISearchSuggestionProvider? _suggestions;
    private readonly SearchEngineSelectorViewModel? _searchEngines;
    private readonly ManageSiteZoom? _siteZoom;
    private readonly DownloadsViewModel? _downloads;
    private readonly bool _isPrivate;

    private BrowserTab _tab = new(TabId.New());
    private IBrowserEngineSession? _session;
    private readonly DispatcherTimer _loadingProgressTimer;
    private int _sessionGeneration;
    private int _progressGeneration;
    private PageAddress? _lastStartedAddress;

    /// <summary>
    /// How many engine history steps this tab has taken since it last left a
    /// local page (New Tab or Downloads), or <see cref="NotAnchoredToNewTab"/>
    /// when it is not sitting above one.
    /// </summary>
    /// <remarks>
    /// The native web view never navigates to those pages — they are surfaces
    /// this application draws instead of a document — so they are absent from
    /// the engine's history, and the engine cannot be asked where the boundary
    /// is. Counting the steps is what puts them back: at zero, Back restores
    /// the local page below rather than walking into engine entries from before
    /// this tab last returned there.
    /// </remarks>
    private int _newTabDepth = NotAnchoredToNewTab;

    private const int NotAnchoredToNewTab = -1;

    /// <summary>
    /// Local pages (and the engine document under Downloads) waiting behind the
    /// current surface. The engine cannot report these, so Back reads here when
    /// it has run out of engine steps — or when the current page is itself local.
    /// </summary>
    private readonly Stack<LocalBackEntry> _localBack = new();

    /// <summary>
    /// The page this tab stepped back from when it returned to a local surface,
    /// kept so going forward can return to it. Cleared as soon as the tab
    /// navigates somewhere else, which is what discards a forward entry in any
    /// browser.
    /// </summary>
    private BlankStepBack? _newTabForward;

    /// <summary>
    /// Set around a history traversal this view model asked for, so the
    /// navigation it raises is not mistaken for a step further from New Tab.
    /// </summary>
    private bool _isTraversing;

    /// <summary>
    /// Set before an engine Navigate this view model already counted, so
    /// <see cref="OnNavigationStarted"/> does not count further steps until
    /// that load finishes. A single ignore bit was consumed by the first
    /// started event; a second one (the document the engine still held under
    /// New Tab, or a redirect) then looked like a link click, so Back walked
    /// into that leftover URL instead of New Tab.
    /// </summary>
    private bool _suppressEngineHistory;

    private enum LocalBackKind
    {
        NewTab,
        Downloads,
        Settings,
        History,
        Engine,
    }

    private sealed record LocalBackEntry(LocalBackKind Kind, BlankStepBack? Page, int EngineDepth);

    /// <summary>What returning forward from a local page has to put back on the tab.</summary>
    private sealed record BlankStepBack(
        PageAddress Address,
        string? Title,
        string? SearchTerm,
        PageAddress? FaviconAddress,
        int EngineDepth);

    public BrowserViewModel(
        NavigateFromAddressBar navigateFromAddressBar,
        NewTabShortcutHub? shortcutHub = null,
        NewTabAmbientViewModel? ambient = null,
        SearchEngineSelectorViewModel? searchEngines = null,
        ISearchSuggestionProvider? suggestions = null,
        ManageSiteZoom? siteZoom = null,
        bool isPrivate = false,
        IPrivacyPreferenceStore? privacy = null,
        DownloadsViewModel? downloads = null)
    {
        _navigateFromAddressBar = navigateFromAddressBar
            ?? throw new ArgumentNullException(nameof(navigateFromAddressBar));
        _suggestions = suggestions;
        _searchEngines = searchEngines;
        _siteZoom = siteZoom;
        _downloads = downloads;
        _isPrivate = isPrivate;
        _loadingProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _loadingProgressTimer.Tick += OnLoadingProgressTick;
        ErrorPage = new NavigationErrorPageViewModel(RetryNavigation, ReturnToNewTab);
        ErrorPage.PropertyChanged += OnErrorPagePropertyChanged;
        NewTab = new NewTabPageViewModel(
            shortcutHub,
            ambient,
            searchEngines,
            suggestions,
            NavigateFromNewTab,
            NavigateTo,
            isPrivate,
            privacy);
    }

    public NewTabPageViewModel NewTab { get; }

    /// <summary>The shared download list, shown when this tab is the downloads page.</summary>
    public DownloadsViewModel? Downloads => _downloads;

    public bool IsPrivate => _isPrivate;

    /// <summary>Local error surface shown instead of an engine-specific failure page.</summary>
    public NavigationErrorPageViewModel ErrorPage { get; }

    /// <summary>Whether the native surface should remain visible behind browser UI.</summary>
    public bool ShouldShowWebView => !IsBlank && !HasInternalPage && !ErrorPage.IsVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShouldShowWebView))]
    private bool _isDownloadsPage;

    /// <summary>
    /// Turns this tab into the local downloads page. The engine is not asked to
    /// move. Pass <paramref name="recordHistory"/> when the user left a surface
    /// they should be able to return to — New Tab via the address bar, or the
    /// document still sitting underneath. A tab created for Downloads (Ctrl+J,
    /// session restore) has no such surface; recording one made Back invent a
    /// New Tab and leave <c>aphelion://downloads</c> in the address bar.
    /// </summary>
    public void ShowDownloads(bool recordHistory = true)
    {
        if (_tab.IsDownloadsPage)
        {
            AddressText = InternalPages.DownloadsAddress;
            return;
        }

        ErrorPage.Hide();
        if (recordHistory)
        {
            PushCurrentToLocalBack();
        }

        _tab.ShowDownloads();
        _newTabDepth = NotAnchoredToNewTab;
        _newTabForward = null;
        SyncFromTab();
    }

    /// <summary>
    /// Binds this view model to the tab it drives. The shell owns tab lifetime, so
    /// the tab is supplied rather than created here.
    /// </summary>
    public void Bind(BrowserTab tab)
    {
        _tab = tab ?? throw new ArgumentNullException(nameof(tab));
        ApplySiteZoom();
        SyncFromTab();
    }

    /// <summary>Current page title, surfaced so the tab strip can follow it.</summary>
    public string PageTitle => _tab.DisplayTitle;

    /// <summary>
    /// Navigates directly to an address, bypassing the address bar. Used when a tab
    /// arrives from another window and has to reload, since a native web view
    /// cannot be carried across windows.
    /// </summary>
    public void NavigateTo(PageAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        ErrorPage.Hide();

        var leftLocal = _tab.IsLocalSurface;
        if (leftLocal)
        {
            PushCurrentToLocalBack();
        }

        _suppressEngineHistory = true;
        _tab.BeginNavigation(address);
        ApplySiteZoom();
        _session?.Navigate(address);
        NoteNavigationAway(leftLocal);
        SyncFromTab();
    }

    private bool NavigateFromNewTab(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        if (TryOpenInternalPage(query))
        {
            return true;
        }

        return NavigateThroughEngine(query);
    }

    /// <summary>
    /// Opens an <c>aphelion://</c> page in this tab. The engine never sees these
    /// addresses; adding settings later is another <see cref="InternalPageKind"/>.
    /// </summary>
    private bool TryOpenInternalPage(string? input)
    {
        if (!InternalPages.TryMatch(input, out var kind))
        {
            return false;
        }

        switch (kind)
        {
            case InternalPageKind.Downloads:
                ShowDownloads();
                return true;
            case InternalPageKind.Settings:
                ShowSettings();
                return true;
            case InternalPageKind.History:
                ShowHistory();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Records a navigation the user asked for: leaving a local page anchors the
    /// tab to it, and any other navigation is one more step away from wherever it
    /// is anchored. Either way the forward entry that page held is now gone.
    /// </summary>
    private void NoteNavigationAway(bool leftLocal)
    {
        _newTabForward = null;

        if (leftLocal)
        {
            _newTabDepth = 0;
        }
        else if (_newTabDepth >= 0)
        {
            _newTabDepth++;
        }
    }

    /// <summary>
    /// Pushes the surface the user is looking at so Back can restore it. Local
    /// pages are not engine history entries; the document under Downloads is not
    /// either, because showing Downloads never moved the engine.
    /// </summary>
    private void PushCurrentToLocalBack()
    {
        if (_tab.IsBlank)
        {
            _localBack.Push(new LocalBackEntry(LocalBackKind.NewTab, null, _newTabDepth));
            return;
        }

        if (_tab.InternalPage is { } page)
        {
            var kind = page switch
            {
                InternalPageKind.Settings => LocalBackKind.Settings,
                InternalPageKind.History => LocalBackKind.History,
                _ => LocalBackKind.Downloads,
            };
            _localBack.Push(new LocalBackEntry(kind, null, _newTabDepth));
            return;
        }

        if (_tab.Address is { } address)
        {
            _localBack.Push(new LocalBackEntry(
                LocalBackKind.Engine,
                new BlankStepBack(address, _tab.Title, _tab.SearchTerm, _tab.FaviconAddress, _newTabDepth),
                _newTabDepth));
        }
    }

    private bool NavigateThroughEngine(string? input)
    {
        if (_session is null)
        {
            return false;
        }

        var leftLocal = _tab.IsLocalSurface;
        if (leftLocal)
        {
            PushCurrentToLocalBack();
        }

        // Count this step before the engine reports it — Navigate may raise
        // started asynchronously, after BeginNavigation has already cleared
        // IsBlank, which would otherwise look like a second step.
        _suppressEngineHistory = true;
        if (!_navigateFromAddressBar.Execute(_tab, _session, input))
        {
            _suppressEngineHistory = false;
            if (leftLocal && _localBack.Count > 0)
            {
                _localBack.Pop();
            }

            return false;
        }

        NoteNavigationAway(leftLocal);
        SyncFromTab();
        return true;
    }

    /// <summary>
    /// Reloads the tab's page once an engine session attaches. The engine surface
    /// is created fresh every time the view attaches, so whatever page the tab
    /// held has to be loaded again — this is why switching back to a tab reloads
    /// it, and why a tab adopted from another window starts loading on arrival.
    /// </summary>
    private void ResumePendingNavigation()
    {
        if (_session is null || _tab.IsLocalSurface || _tab.Address is not { } address)
        {
            return;
        }

        _suppressEngineHistory = true;
        _tab.BeginNavigation(address);
        ApplySiteZoom();
        _session.Navigate(address);
        SyncFromTab();
    }

    /// <summary>Text currently in the address bar, which the user may be editing.</summary>
    [ObservableProperty]
    private string _addressText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopLoadingCommand))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomLabel))]
    private int _zoomPercent = PageZoom.DefaultPercent;

    public string ZoomLabel => $"{ZoomPercent}%";

    /// <summary>
    /// Requests window-level feedback whenever this browser's effective zoom
    /// changes. The shell owns the toast lifetime so changing tabs or split-pane
    /// focus cannot detach the visual from the event that produced it.
    /// </summary>
    public event EventHandler<ZoomFeedbackRequestedEventArgs>? ZoomFeedbackRequested;

    /// <summary>
    /// Raised when a page in this tab starts a download, after the shared
    /// download list has taken it. The shell listens so the window the download
    /// started in — and only that window — opens its downloads panel, as Chrome
    /// surfaces its bubble.
    /// </summary>
    public event EventHandler? DownloadStarted;

    /// <summary>
    /// True while an HTML element in this tab is using the Fullscreen API.
    /// The window hides its chrome in response so the video can fill the screen.
    /// </summary>
    [ObservableProperty]
    private bool _containsFullScreenElement;

    /// <summary>
    /// Raised for F11 and Escape while this tab's page has native focus.
    /// Must be handled synchronously so WebView2 can be told not to eat the key.
    /// </summary>
    public event EventHandler<EngineAcceleratorKeyPressedEventArgs>? AcceleratorKeyPressed;

    /// <summary>Simulated, monotonic navigation progress from 0 to 100.</summary>
    [ObservableProperty]
    private double _loadingProgress;

    /// <summary>Remains visible briefly at 100 so completion never looks abrupt.</summary>
    [ObservableProperty]
    private bool _isLoadingProgressVisible;

    [ObservableProperty]
    private bool _isBlank = true;

    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    [ObservableProperty]
    private bool _canGoBack;

    [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
    [ObservableProperty]
    private bool _canGoForward;

    /// <summary>
    /// Connects the view's engine session. Called by the view once the native web
    /// view exists, since the engine surface cannot be created before then.
    /// </summary>
    public void AttachSession(IBrowserEngineSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (ReferenceEquals(_session, session))
        {
            return;
        }

        DetachCurrentSession();
        _session = session;
        _sessionGeneration++;
        _session.NavigationStarted += OnNavigationStarted;
        _session.NavigationCompleted += OnNavigationCompleted;
        _session.ZoomFactorChanged += OnZoomFactorChanged;
        _session.DownloadStarted += OnDownloadStarted;
        _session.FullScreenElementChanged += OnFullScreenElementChanged;
        _session.AcceleratorKeyPressed += OnAcceleratorKeyPressed;
        _session.PageMessage += OnPageMessage;

        RefreshHistoryState();
        ResumePendingNavigation();
        _ = ApplyMuteAsync();
    }

    /// <summary>
    /// Releases the engine surface when its view is reused for another tab.
    /// Delayed script results from that surface are invalidated at the same time,
    /// so one tab can never inherit another page's title or favicon.
    /// </summary>
    public void DetachSession(IBrowserEngineSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!ReferenceEquals(_session, session))
        {
            return;
        }

        DetachCurrentSession();
        RefreshHistoryState();
    }

    [RelayCommand]
    private void Navigate()
    {
        if (TryOpenInternalPage(AddressText))
        {
            return;
        }

        NavigateThroughEngine(AddressText);
    }

    [RelayCommand]
    private void CancelAddressEditing()
    {
        AddressText = AddressBarText();
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        // Local pages, and the first engine document above one, are restored
        // from the stack — not from the engine, which never saw those pages and
        // may still hold entries from before this tab last returned to them.
        if (_tab.IsLocalSurface || _newTabDepth == 0 || _session?.CanGoBack != true)
        {
            RestoreLocalBack();
            return;
        }

        if (_newTabDepth > 0)
        {
            _newTabDepth--;
        }

        _isTraversing = true;
        _session.GoBack();
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        // Forward out of a local page is the mirror of the step that got here:
        // the engine never moved, so the page it is still showing only has to be
        // put back on the tab.
        if (_tab.IsLocalSurface && _newTabForward is { } resume)
        {
            PushCurrentToLocalBack();
            _tab.RestoreFromBlank(
                resume.Address,
                resume.Title,
                resume.SearchTerm,
                resume.FaviconAddress);

            _newTabForward = null;
            _newTabDepth = resume.EngineDepth;
            ApplySiteZoom();
            SyncFromTab();
            return;
        }

        if (_session?.CanGoForward != true)
        {
            return;
        }

        if (_newTabDepth >= 0)
        {
            _newTabDepth++;
        }

        _isTraversing = true;
        _session.GoForward();
    }

    [RelayCommand(CanExecute = nameof(CanReload))]
    private void Reload()
    {
        if (_tab.IsLocalSurface)
        {
            return;
        }

        _session?.Reload();
    }

    private bool CanReload() => !IsLoading && !_tab.IsLocalSurface;

    [RelayCommand]
    private void ZoomIn() => SetZoom(PageZoom.FromPercent(ZoomPercent).Increase());

    [RelayCommand]
    private void ZoomOut() => SetZoom(PageZoom.FromPercent(ZoomPercent).Decrease());

    [RelayCommand]
    private void ResetZoom() => SetZoom(PageZoom.Default);

    [RelayCommand(CanExecute = nameof(IsLoading))]
    private void StopLoading()
    {
        _session?.StopLoading();

        if (_tab.LoadState == TabLoadState.Loading)
        {
            _tab.CancelNavigation();
            SyncFromTab();
        }
    }

    private void OnNavigationStarted(object? sender, EngineNavigationStartedEventArgs e)
    {
        if (!ReferenceEquals(sender, _session))
        {
            return;
        }

        // Navigation can also start from inside the page — a link click, a redirect.
        // Mirror it into the tab so the address bar tracks where we actually are.
        if (PageAddress.TryCreate(e.RequestedUrl, out var address) && address is not null)
        {
            // A step the page took for itself is one more step from New Tab, and
            // it drops whatever was ahead. A traversal this view model asked for
            // has already accounted for itself, and a redirect replaces the entry
            // it came from rather than adding one. Suppression lasts until the
            // load completes so a second started event cannot sneak a leftover
            // document into the count.
            var isRedirect = _lastStartedAddress is not null && _tab.LoadState == TabLoadState.Loading;

            if (_isTraversing)
            {
                _isTraversing = false;
            }
            else if (!_suppressEngineHistory && !isRedirect)
            {
                NoteNavigationAway(_tab.IsLocalSurface);
            }

            _lastStartedAddress = address;
            _tab.BeginNavigation(address);
            ApplySiteZoom();
        }

        SyncFromTab();
    }

    private async void OnNavigationCompleted(object? sender, EngineNavigationCompletedEventArgs e)
    {
        if (!ReferenceEquals(sender, _session))
        {
            return;
        }

        // Native controls can report their initial/previous document while a
        // blank tab is taking ownership of the surface. A blank domain tab has no
        // navigation to complete and must never inherit that document's identity.
        // A completion for that leftover document must not lift history
        // suppression either: the next Navigate is still in flight.
        if (_tab.Address is not { } current)
        {
            RefreshHistoryState();
            return;
        }

        PageAddress.TryCreate(e.RequestedUrl, out var completedAddress);

        if (!e.IsSuccess && e.RequestedUrl is null)
        {
            return;
        }

        completedAddress ??= e.RequestedUrl is null ? _lastStartedAddress : null;

        if (completedAddress is null)
        {
            return;
        }

        if (!completedAddress.Equals(current) &&
            !AreLoopbackAliases(completedAddress.Value, current.Value))
        {
            return;
        }

        _suppressEngineHistory = false;
        _lastStartedAddress = null;

        // Chrome leaves the engine document on screen: a real page, a challenge,
        // or Chromium's own "can't be reached" page. An Avalonia overlay on top
        // of that is what hid CAPTCHAs and disagreed with localhost vs 127.0.0.1.
        if (!completedAddress.Equals(current))
        {
            _tab.BeginNavigation(completedAddress);
        }

        _tab.CompleteNavigation(title: null);
        SyncFromTab();
        RecordHistoryIfNeeded();
        await ReadPageIdentityAsync();
    }

    /// <summary>
    /// Reads the page's title and favicon from the document.
    /// </summary>
    /// <remarks>
    /// The engine raises no event for either, so both are pulled with a script
    /// once navigation completes. The favicon href is resolved against the
    /// document so relative paths work, falling back to the site's /favicon.ico.
    /// </remarks>
    private async Task ReadPageIdentityAsync()
    {
        var session = _session;

        if (session is null)
        {
            return;
        }

        var generation = _sessionGeneration;
        var navigated = _tab.Address;

        if (navigated is null)
        {
            return;
        }

        // A literal separator rather than a newline: the result comes back as a
        // JSON string, where a newline arrives escaped and would not split.
        var result = await session.EvaluateAsync(
            """
            (function () {
              var icon = document.querySelector("link[rel~='icon']");
              var href = icon ? new URL(icon.getAttribute('href'), document.baseURI).href
                              : new URL('/favicon.ico', document.baseURI).href;
              return location.href + '|@|' + document.title + '|@|' + href;
            })()
            """);

        // The user may have navigated again while the script ran; a late result
        // would relabel the wrong page.
        if (result is null ||
            generation != _sessionGeneration ||
            !ReferenceEquals(session, _session) ||
            _tab.Address != navigated)
        {
            return;
        }

        var parts = result.Trim().Trim('"').Split("|@|", StringSplitOptions.None);

        // The native surface may have been reused quickly enough that a script
        // ran against a different document on the same control. Session identity
        // alone cannot detect that; the document URL must match too.
        if (parts.Length < 2 ||
            !Uri.TryCreate(parts[0].Trim(), UriKind.Absolute, out var documentUri) ||
            !PageAddress.TryCreate(documentUri, out var documentAddress) ||
            documentAddress is null ||
            !documentAddress.Equals(navigated))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(parts[1]))
        {
            _tab.UpdateTitle(parts[1].Trim());
        }

        if (parts.Length > 2 &&
            Uri.TryCreate(parts[2].Trim(), UriKind.Absolute, out var iconUri) &&
            PageAddress.TryCreate(iconUri, out var icon))
        {
            _tab.UpdateFavicon(icon);
        }

        SyncFromTab();
        OnPropertyChanged(nameof(FaviconAddress));
    }

    private string AddressBarText() =>
        _tab.InternalPage is { } page
            ? InternalPages.AddressOf(page)
            : _tab.Address?.ToString() ?? string.Empty;

    /// <summary>The current page's favicon address, surfaced for the tab strip.</summary>
    public PageAddress? FaviconAddress => _tab.FaviconAddress;

    /// <summary>
    /// The page currently open, or null on a blank tab. Surfaced for the toolbar
    /// star, which has to know whether this address is already bookmarked.
    /// </summary>
    public PageAddress? Address => _tab.Address;

    private void SyncFromTab()
    {
        var wasLoading = IsLoading;
        IsLoading = _tab.LoadState == TabLoadState.Loading;
        IsBlank = _tab.IsBlank;
        IsDownloadsPage = _tab.IsDownloadsPage;
        RefreshOmnibox();
        OnPropertyChanged(nameof(ShouldShowWebView));

        if (IsLoading)
        {
            ErrorPage.Hide();

            if (!wasLoading)
            {
                BeginLoadingProgress();
            }
        }
        else if (wasLoading)
        {
            CompleteLoadingProgress();
        }

        ErrorPage.Hide();

        // Do not overwrite the address bar while the user is typing into it: only
        // follow the tab when a navigation is in flight or has just landed.
        if (_tab.HasInternalPage || _tab.Address is not null)
        {
            AddressText = AddressBarText();
        }

        StatusText = _tab.LoadState switch
        {
            // Loading is represented by the chrome progress indicator. Keeping
            // this empty prevents a second, disconnected status bar at the page
            // bottom while preserving failure feedback below.
            TabLoadState.Loading => string.Empty,
            // The local error page owns failure copy as well, so native failure
            // text never reappears below the content area.
            TabLoadState.Failed => string.Empty,
            _ => string.Empty,
        };

        // PageTitle and Address are derived from the tab rather than stored, so
        // they have to be raised by hand whenever the tab changes underneath them.
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(Address));

        // Every caller of SyncFromTab that changes _cameFromNewTab or the tab's
        // address needs CanGoBack recomputed; folding it in here means no call
        // site can change one without the other; a bare SyncFromTab used to leave
        // the Back button stuck disabled after a New Tab -> page navigation until
        // something else happened to call RefreshHistoryState afterwards.
        RefreshHistoryState();
    }

    private void RefreshHistoryState()
    {
        // Local pages are not in the engine's history. Depth counts engine steps
        // above one; the stack holds the pages themselves. An unanchored restored
        // tab still uses the engine, because it never sat on New Tab.
        CanGoBack = _localBack.Count > 0
            || _newTabDepth >= 0
            || (!_tab.IsLocalSurface && (_session?.CanGoBack ?? false));
        CanGoForward = (_session?.CanGoForward ?? false) ||
            (_tab.IsLocalSurface && _newTabForward is not null);
    }

    /// <summary>
    /// Clears this tab's cookies if it is private and currently has an attached
    /// engine surface. Called when the window holding it closes — see
    /// <see cref="WindowManager.CreatePrivateWindow"/> — since a private tab that
    /// never leaves its window otherwise never has a moment to clean up.
    /// </summary>
    public Task ClearPrivateBrowsingDataAsync() =>
        _isPrivate && _session is { } session ? session.ClearBrowsingDataAsync() : Task.CompletedTask;

    private void DetachCurrentSession()
    {
        if (_session is null)
        {
            return;
        }

        _session.NavigationStarted -= OnNavigationStarted;
        _session.NavigationCompleted -= OnNavigationCompleted;
        _session.ZoomFactorChanged -= OnZoomFactorChanged;
        _session.DownloadStarted -= OnDownloadStarted;
        _session.FullScreenElementChanged -= OnFullScreenElementChanged;
        _session.AcceleratorKeyPressed -= OnAcceleratorKeyPressed;
        _session.PageMessage -= OnPageMessage;
        ContainsFullScreenElement = false;
        _session = null;
        _lastStartedAddress = null;
        _sessionGeneration++;
        ResetLoadingProgress();
    }

    private void BeginLoadingProgress()
    {
        _progressGeneration++;
        _loadingProgressTimer.Start();
        LoadingProgress = 9;
        IsLoadingProgressVisible = true;
    }

    private void OnLoadingProgressTick(object? sender, EventArgs e)
    {
        // Native WebViews do not expose portable byte-level progress. Move
        // quickly at first then asymptotically approach completion, reserving
        // the final portion for the real completed-navigation event.
        LoadingProgress = Math.Min(
            92,
            LoadingProgress + Math.Max(0.35, (92 - LoadingProgress) * 0.08));
    }

    private void CompleteLoadingProgress()
    {
        _loadingProgressTimer.Stop();
        LoadingProgress = 100;
        var generation = ++_progressGeneration;
        _ = HideCompletedProgressAsync(generation);
    }

    private async Task HideCompletedProgressAsync(int generation)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(180)).ConfigureAwait(true);

        if (generation == _progressGeneration && !IsLoading)
        {
            IsLoadingProgressVisible = false;
            LoadingProgress = 0;
        }
    }

    private void ResetLoadingProgress()
    {
        _progressGeneration++;
        _loadingProgressTimer.Stop();
        IsLoadingProgressVisible = false;
        LoadingProgress = 0;
    }

    private void RetryNavigation()
    {
        if (_tab.Address is { } address)
        {
            NavigateTo(address);
        }
    }

    private static bool AreLoopbackAliases(Uri left, Uri right) =>
        left.Port == right.Port &&
        IsLoopbackHost(left.Host) &&
        IsLoopbackHost(right.Host);

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "127.0.0.1", StringComparison.Ordinal) ||
        string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);

    private void ReturnToNewTab()
    {
        _session?.StopLoading();

        // Kept before the reset clears it, so forward can put the tab back on the
        // page the engine is still showing.
        _newTabForward = CaptureCurrentPage();

        _tab.ResetToBlank();
        AddressText = string.Empty;
        ApplySiteZoom();
        ErrorPage.Hide();

        // New Tab is the floor of this tab's local history.
        _newTabDepth = NotAnchoredToNewTab;
        _localBack.Clear();
        _suppressEngineHistory = false;

        SyncFromTab();
        RefreshHistoryState();
    }

    private void RestoreLocalBack()
    {
        if (_localBack.Count == 0)
        {
            ReturnToNewTab();
            return;
        }

        var entry = _localBack.Pop();
        switch (entry.Kind)
        {
            case LocalBackKind.Downloads:
                RevealInternal(InternalPageKind.Downloads);
                break;

            case LocalBackKind.Settings:
                RevealInternal(InternalPageKind.Settings);
                break;

            case LocalBackKind.History:
                RevealInternal(InternalPageKind.History);
                break;

            case LocalBackKind.Engine when entry.Page is not null:
                RevealEnginePage(entry.Page);
                break;

            default:
                ReturnToNewTab();
                break;
        }
    }

    private void RevealInternal(InternalPageKind kind)
    {
        _session?.StopLoading();
        _newTabForward = CaptureCurrentPage();
        _tab.ShowInternal(kind);
        _newTabDepth = NotAnchoredToNewTab;
        ErrorPage.Hide();
        SyncFromTab();
    }

    private void RevealDownloads() => RevealInternal(InternalPageKind.Downloads);

    private void RevealEnginePage(BlankStepBack page)
    {
        _tab.RestoreFromBlank(page.Address, page.Title, page.SearchTerm, page.FaviconAddress);
        _newTabDepth = page.EngineDepth;
        ApplySiteZoom();
        ErrorPage.Hide();
        SyncFromTab();
    }

    private BlankStepBack? CaptureCurrentPage() =>
        _tab.Address is { } leaving
            ? new BlankStepBack(
                leaving,
                _tab.Title,
                _tab.SearchTerm,
                _tab.FaviconAddress,
                _newTabDepth < 0 ? 0 : _newTabDepth)
            : null;

    private void OnErrorPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationErrorPageViewModel.IsVisible))
        {
            OnPropertyChanged(nameof(ShouldShowWebView));
        }
    }

    private void ApplySiteZoom()
    {
        var zoom = _siteZoom?.Resolve(_tab.Address) ?? PageZoom.Default;
        var appliedFactor = _session?.SetZoomFactor(zoom.Factor) ?? zoom.Factor;
        ZoomPercent = FromEngineFactor(appliedFactor).Percent;
    }

    private void SetZoom(PageZoom zoom)
    {
        var appliedFactor = _session?.SetZoomFactor(zoom.Factor) ?? zoom.Factor;
        var appliedZoom = FromEngineFactor(appliedFactor);
        appliedZoom = _siteZoom?.Save(_tab.Address, appliedZoom) ?? appliedZoom;
        ZoomPercent = appliedZoom.Percent;
        RequestZoomFeedback(appliedZoom.Percent);
    }

    private void OnZoomFactorChanged(object? sender, EngineZoomFactorChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _session) || !double.IsFinite(e.Factor))
        {
            return;
        }

        void Apply()
        {
            if (!ReferenceEquals(sender, _session))
            {
                return;
            }

            var zoom = FromEngineFactor(e.Factor);
            zoom = _siteZoom?.Save(_tab.Address, zoom) ?? zoom;
            ZoomPercent = zoom.Percent;
            RequestZoomFeedback(zoom.Percent);
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private void OnDownloadStarted(object? sender, EngineDownloadStartedEventArgs e)
    {
        if (!ReferenceEquals(sender, _session))
        {
            return;
        }

        void Apply()
        {
            if (!ReferenceEquals(sender, _session))
            {
                return;
            }

            _downloads?.Track(e.Operation, _isPrivate);
            DownloadStarted?.Invoke(this, EventArgs.Empty);
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private void OnFullScreenElementChanged(object? sender, EngineFullScreenElementChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _session))
        {
            return;
        }

        void Apply()
        {
            if (!ReferenceEquals(sender, _session))
            {
                return;
            }

            ContainsFullScreenElement = e.ContainsFullScreenElement;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private void OnAcceleratorKeyPressed(object? sender, EngineAcceleratorKeyPressedEventArgs e)
    {
        if (!ReferenceEquals(sender, _session))
        {
            return;
        }

        AcceleratorKeyPressed?.Invoke(this, e);
    }

    private void OnPageMessage(object? sender, EnginePageMessageEventArgs e)
    {
        if (!ReferenceEquals(sender, _session))
        {
            return;
        }

        void Apply() => HandlePageMessage(e);

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private void RequestZoomFeedback(int percent) =>
        ZoomFeedbackRequested?.Invoke(this, new ZoomFeedbackRequestedEventArgs(percent));

    private static PageZoom FromEngineFactor(double factor) =>
        PageZoom.FromPercent((int)Math.Round(factor * 100d, MidpointRounding.AwayFromZero));
}

public sealed class ZoomFeedbackRequestedEventArgs(int percent) : EventArgs
{
    public int Percent { get; } = percent;
}
