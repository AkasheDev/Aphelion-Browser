using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Application.UseCases;
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

    private BrowserTab _tab = new(TabId.New());
    private IBrowserEngineSession? _session;
    private readonly DispatcherTimer _loadingProgressTimer;
    private int _sessionGeneration;
    private int _progressGeneration;
    private PageAddress? _lastStartedAddress;

    public BrowserViewModel(
        NavigateFromAddressBar navigateFromAddressBar,
        NewTabShortcutHub? shortcutHub = null,
        NewTabAmbientViewModel? ambient = null,
        SearchEngineSelectorViewModel? searchEngines = null,
        ISearchSuggestionProvider? suggestions = null)
    {
        _navigateFromAddressBar = navigateFromAddressBar
            ?? throw new ArgumentNullException(nameof(navigateFromAddressBar));
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
            NavigateTo);
    }

    public NewTabPageViewModel NewTab { get; }

    /// <summary>Local error surface shown instead of an engine-specific failure page.</summary>
    public NavigationErrorPageViewModel ErrorPage { get; }

    /// <summary>Whether the native surface should remain visible behind browser UI.</summary>
    public bool ShouldShowWebView => !IsBlank && !ErrorPage.IsVisible;

    /// <summary>
    /// Binds this view model to the tab it drives. The shell owns tab lifetime, so
    /// the tab is supplied rather than created here.
    /// </summary>
    public void Bind(BrowserTab tab)
    {
        _tab = tab ?? throw new ArgumentNullException(nameof(tab));
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
        _tab.BeginNavigation(address);
        _session?.Navigate(address);
        SyncFromTab();
    }

    private bool NavigateFromNewTab(string query)
    {
        if (_session is null || string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var navigated = _navigateFromAddressBar.Execute(_tab, _session, query);

        if (navigated)
        {
            SyncFromTab();
        }

        return navigated;
    }

    /// <summary>
    /// Reloads the tab's page once an engine session attaches. The engine surface
    /// is created fresh every time the view attaches, so whatever page the tab
    /// held has to be loaded again — this is why switching back to a tab reloads
    /// it, and why a tab adopted from another window starts loading on arrival.
    /// </summary>
    private void ResumePendingNavigation()
    {
        if (_session is not null && _tab.Address is { } address)
        {
            _tab.BeginNavigation(address);
            _session.Navigate(address);
            SyncFromTab();
        }
    }

    /// <summary>Text currently in the address bar, which the user may be editing.</summary>
    [ObservableProperty]
    private string _addressText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

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

        RefreshHistoryState();
        ResumePendingNavigation();
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
        if (_session is null)
        {
            return;
        }

        if (_navigateFromAddressBar.Execute(_tab, _session, AddressText))
        {
            SyncFromTab();
        }
    }

    [RelayCommand]
    private void CancelAddressEditing()
    {
        AddressText = _tab.Address?.ToString() ?? string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => _session?.GoBack();

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward() => _session?.GoForward();

    [RelayCommand]
    private void Reload()
    {
        if (_tab.IsBlank)
        {
            return;
        }

        _session?.Reload();
    }

    [RelayCommand]
    private void StopLoading() => _session?.StopLoading();

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
            _lastStartedAddress = address;
            _tab.BeginNavigation(address);
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
        if (_tab.Address is null)
        {
            RefreshHistoryState();
            return;
        }

        PageAddress.TryCreate(e.RequestedUrl, out var completedAddress);
        completedAddress ??= e.RequestedUrl is null ? _lastStartedAddress : null;

        if (completedAddress is null ||
            !completedAddress.Equals(_tab.Address))
        {
            return;
        }

        _lastStartedAddress = null;

        if (e.IsSuccess)
        {
            _tab.CompleteNavigation(title: null);
        }
        else
        {
            _tab.FailNavigation(e.FailureReason);
        }

        SyncFromTab();
        RefreshHistoryState();

        if (e.IsSuccess)
        {
            await ReadPageIdentityAsync();
        }
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

    /// <summary>The current page's favicon address, surfaced for the tab strip.</summary>
    public PageAddress? FaviconAddress => _tab.FaviconAddress;

    private void SyncFromTab()
    {
        var wasLoading = IsLoading;
        IsLoading = _tab.LoadState == TabLoadState.Loading;
        IsBlank = _tab.IsBlank;
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

        if (_tab.LoadState == TabLoadState.Failed)
        {
            ErrorPage.Present(_tab.Address, _tab.FailureReason);
        }

        // Do not overwrite the address bar while the user is typing into it: only
        // follow the tab when a navigation is in flight or has just landed.
        if (_tab.Address is not null)
        {
            AddressText = _tab.Address.ToString();
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

        // PageTitle is derived from the tab rather than stored, so it has to be
        // raised by hand whenever the tab changes underneath it.
        OnPropertyChanged(nameof(PageTitle));
    }

    private void RefreshHistoryState()
    {
        CanGoBack = _session?.CanGoBack ?? false;
        CanGoForward = _session?.CanGoForward ?? false;
    }

    private void DetachCurrentSession()
    {
        if (_session is null)
        {
            return;
        }

        _session.NavigationStarted -= OnNavigationStarted;
        _session.NavigationCompleted -= OnNavigationCompleted;
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

    private void ReturnToNewTab()
    {
        _session?.StopLoading();
        _tab.ResetToBlank();
        ErrorPage.Hide();
        SyncFromTab();
    }

    private void OnErrorPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationErrorPageViewModel.IsVisible))
        {
            OnPropertyChanged(nameof(ShouldShowWebView));
        }
    }

}
