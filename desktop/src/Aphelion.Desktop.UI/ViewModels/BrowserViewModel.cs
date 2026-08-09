using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Application.UseCases;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
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

    public BrowserViewModel(NavigateFromAddressBar navigateFromAddressBar)
    {
        _navigateFromAddressBar = navigateFromAddressBar
            ?? throw new ArgumentNullException(nameof(navigateFromAddressBar));
    }

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

        _tab.BeginNavigation(address);
        _session?.Navigate(address);
        SyncFromTab();
    }

    /// <summary>
    /// Replays the pending address once the engine session attaches. A tab adopted
    /// from another window is told where to go before its view exists.
    /// </summary>
    private void ResumePendingNavigation()
    {
        if (_session is not null && _tab.Address is { } address && _tab.LoadState == TabLoadState.Loading)
        {
            _session.Navigate(address);
        }
    }

    /// <summary>Text currently in the address bar, which the user may be editing.</summary>
    [ObservableProperty]
    private string _addressText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

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

        if (_session is not null)
        {
            _session.NavigationStarted -= OnNavigationStarted;
            _session.NavigationCompleted -= OnNavigationCompleted;
        }

        _session = session;
        _session.NavigationStarted += OnNavigationStarted;
        _session.NavigationCompleted += OnNavigationCompleted;

        RefreshHistoryState();
        ResumePendingNavigation();
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
        // Navigation can also start from inside the page — a link click, a redirect.
        // Mirror it into the tab so the address bar tracks where we actually are.
        if (PageAddress.TryCreate(e.RequestedUrl, out var address) && address is not null)
        {
            _tab.BeginNavigation(address);
        }

        SyncFromTab();
    }

    private void OnNavigationCompleted(object? sender, EngineNavigationCompletedEventArgs e)
    {
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
    }

    private void SyncFromTab()
    {
        IsLoading = _tab.LoadState == TabLoadState.Loading;
        IsBlank = _tab.IsBlank;

        // Do not overwrite the address bar while the user is typing into it: only
        // follow the tab when a navigation is in flight or has just landed.
        if (_tab.Address is not null)
        {
            AddressText = _tab.Address.ToString();
        }

        StatusText = _tab.LoadState switch
        {
            TabLoadState.Loading => "Loading…",
            TabLoadState.Failed => _tab.FailureReason ?? "Navigation failed.",
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
}
