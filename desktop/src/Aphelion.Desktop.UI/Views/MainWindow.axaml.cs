using System.Collections.Specialized;
using System.ComponentModel;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.UI.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aphelion.Desktop.UI.Views;

public partial class MainWindow : Window
{
    private TabStripDragHandler? _tabDrag;
    private ShellViewModel? _hookedShell;

    /// <summary>F11 browser fullscreen, independent of an HTML video's own fullscreen.</summary>
    private bool _f11FullScreen;

    /// <summary>True while any tab has an HTML element using the Fullscreen API.</summary>
    private bool _htmlFullScreen;

    /// <summary>
    /// Set when HTML fullscreen just ended, so the same Escape that left YouTube
    /// does not also drop an F11 lock.
    /// </summary>
    private bool _suppressF11ExitOnEscape;

    private WindowState _restoreWindowState = WindowState.Normal;
    private bool _applyingFullScreen;

    /// <summary>One live browser view per open tab, kept in the visual tree.</summary>
    private readonly Dictionary<BrowserViewModel, BrowserView> _pool = [];

    public MainWindow()
    {
        InitializeComponent();

        var strip = this.FindControl<ItemsControl>("TabStrip");

        if (strip is not null)
        {
            _tabDrag = new TabStripDragHandler(strip, () => Shell);
        }

        // Tab activation is handled here rather than with a per-tab command: a tab
        // header is a Border, not a Button, because a Button would swallow the
        // press the drag handler needs.
        AddHandler(PointerReleasedEvent, OnPointerReleasedTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnNavigationShortcutTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);

        // The bookmark bar is one view model shared by every window, so a click
        // on it carries no indication of which window it came from. Each shell
        // reports whether its window is in front, and the bar acts on that one.
        Activated += (_, _) => SetWindowActive(true);
        Deactivated += (_, _) => SetWindowActive(false);
        Closed += (_, _) =>
        {
            if (Shell is { } shell)
            {
                shell.ReleaseBookmarks();
            }
        };

        // WebView2 is a native child window on Windows and owns Ctrl+wheel. Its
        // real factor is observed by the browser-engine adapter instead; adding
        // a second Avalonia handler here makes the level depend on whether the
        // pointer happened to be over chrome or page content. Other backends can
        // still use the portable routed gesture until they expose equivalent
        // native zoom-factor events.
        if (!OperatingSystem.IsWindows())
        {
            AddHandler(PointerWheelChangedEvent, OnZoomWheelTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        AddHandler(PointerPressedEvent, OnOverflowDismissTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        HookPaneFocus();
    }

    private ShellViewModel? Shell => (DataContext as MainWindowViewModel)?.Shell;

    private void SetWindowActive(bool active)
    {
        if (Shell is { } shell)
        {
            shell.IsWindowActive = active;

            if (active)
            {
                shell.Bookmarks?.MarkActive(shell);
            }
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_hookedShell is not null)
        {
            _hookedShell.PropertyChanged -= OnShellPropertyChanged;
            _hookedShell.Browsers.CollectionChanged -= OnBrowsersChanged;
            _hookedShell.HtmlFullScreenElementChanged -= OnHtmlFullScreenElementChanged;
            _hookedShell.AcceleratorKeyPressed -= OnAcceleratorKeyPressed;
        }

        _hookedShell = Shell;

        if (_hookedShell is not null)
        {
            _hookedShell.PropertyChanged += OnShellPropertyChanged;

            _hookedShell.Browsers.CollectionChanged += OnBrowsersChanged;
            _hookedShell.HtmlFullScreenElementChanged += OnHtmlFullScreenElementChanged;
            _hookedShell.AcceleratorKeyPressed += OnAcceleratorKeyPressed;

            // "Move to new window" needs the window manager and this window's
            // geometry, so the window supplies it rather than the view model.
            _hookedShell.MoveToNewWindow = MoveTabToNewWindow;
        }

        UpdateSplitLayout();
        UpdateOverflowLayout();
        UpdateZoomFeedbackAnchor();
        UpdateBrowserPool();

        // The window is generally activated before its DataContext arrives, so
        // that first Activated had no shell to tell. Catch up here.
        SetWindowActive(IsActive);
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsSplit))
        {
            UpdateSplitLayout();
        }

        if (e.PropertyName is nameof(ShellViewModel.IsSplit)
            or nameof(ShellViewModel.IsRightPaneFocused))
        {
            UpdateZoomFeedbackAnchor();
        }

        if (e.PropertyName == nameof(ShellViewModel.IsOverflowOpen))
        {
            UpdateOverflowLayout();
        }

        if (e.PropertyName is nameof(ShellViewModel.ActiveBrowser)
            or nameof(ShellViewModel.SplitBrowser)
            or nameof(ShellViewModel.IsSplit))
        {
            UpdateBrowserPool();
        }
    }

    private void OnBrowsersChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateBrowserPool();

    /// <summary>
    /// Keeps one live view per open tab as a child of the browser host, and moves
    /// the two that are on screen into their columns.
    /// </summary>
    /// <remarks>
    /// Done here rather than with an ItemsControl because the views have to be
    /// children of the very grid whose columns the splitter drives; an items panel
    /// of its own would need those column widths mirrored into it, and the mirror
    /// would not follow a drag.
    /// <para>
    /// Views are inserted beneath the pane frames so the frames' accent edges and
    /// the split picker still draw over them. They are inset by the accent's
    /// thickness while split, because a native web view covers whatever Avalonia
    /// draws underneath it — including that edge.
    /// </para>
    /// </remarks>
    private void UpdateBrowserPool()
    {
        if (this.FindControl<Grid>("BrowserHost") is not { } host || Shell is not { } shell)
        {
            return;
        }

        foreach (var (model, view) in _pool.ToList())
        {
            if (!shell.Browsers.Contains(model))
            {
                host.Children.Remove(view);
                view.Dispose();
                _pool.Remove(model);
            }
        }

        var inset = shell.IsSplit ? new Thickness(0, 0, 0, 2) : default;

        foreach (var model in shell.Browsers)
        {
            if (!_pool.TryGetValue(model, out var view))
            {
                view = new BrowserView { DataContext = model };
                _pool[model] = view;

                // Beneath the frames, which are added in the XAML.
                host.Children.Insert(0, view);
            }

            var right = ReferenceEquals(model, shell.SplitBrowser);

            Grid.SetColumn(view, right ? 2 : 0);
            view.IsVisible = right || ReferenceEquals(model, shell.ActiveBrowser);
            view.Margin = inset;
        }
    }

    /// <summary>
    /// Gives the split pane its half of the window. Driven from code because
    /// ColumnDefinitions sit outside the visual tree and cannot carry bindings.
    /// </summary>
    private void UpdateSplitLayout()
    {
        if (this.FindControl<Grid>("BrowserHost") is not { ColumnDefinitions.Count: 3 } host)
        {
            return;
        }

        var split = Shell?.IsSplit == true;

        host.ColumnDefinitions[2].Width = split
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);

        // The floor the divider stops at. It belongs on the columns, not on the
        // panes: the splitter resizes columns, and a pane asking for more room
        // than its column has would simply be clipped. Fixed rather than a share
        // of the window, so a pane is the same size on a television as it is on a
        // laptop at its smallest — a page needs the width a page needs.
        var floor = split ? MinimumPaneWidth : 0;
        host.ColumnDefinitions[0].MinWidth = floor;
        host.ColumnDefinitions[2].MinWidth = floor;
    }

    /// <summary>
    /// The narrowest a split pane may be dragged. Only applied while split: on
    /// its own a pane is the whole window and must follow it down to the
    /// window's own minimum.
    /// </summary>
    private const double MinimumPaneWidth = 345;

    /// <summary>
    /// Keeps zoom feedback over the page controlled by the shared toolbar. The
    /// anchor participates in the same grid as both panes, so the native popup
    /// follows resizing and splitter movement without manual pixel placement.
    /// </summary>
    private void UpdateZoomFeedbackAnchor()
    {
        if (this.FindControl<Border>("ZoomFeedbackAnchor") is not { } anchor)
        {
            return;
        }

        Grid.SetColumn(
            anchor,
            Shell is { IsSplit: true, IsRightPaneFocused: true } ? 2 : 0);
    }

    /// <summary>
    /// Widens the Other Tabs drawer column while the drawer is open and
    /// collapses it to nothing when it is not. Driven from code for the same
    /// reason as the split column above — ColumnDefinitions cannot carry
    /// bindings — and it has to be driven at all because hiding the drawer
    /// Border alone left its column holding the drawer's width, showing a dead
    /// strip beside the page.
    /// </summary>
    private void UpdateOverflowLayout()
    {
        if (this.FindControl<Grid>("ContentHost") is not { ColumnDefinitions.Count: 2 } host)
        {
            return;
        }

        host.ColumnDefinitions[1].Width = Shell?.IsOverflowOpen == true
            ? new GridLength(344)
            : new GridLength(0);
    }

    /// <summary>
    /// Clicking a pane gives it the focus, so the toolbar acts on it. Wired as a
    /// tunnelling handler on the window: a bubbling handler on the pane never
    /// fires, because the native web view consumes the event before it rises.
    /// </summary>
    private void HookPaneFocus() =>
        AddHandler(
            PointerPressedEvent,
            (_, e) =>
            {
                if (Shell is not { IsSplit: true } shell || e.Source is not Visual source)
                {
                    return;
                }

                if (this.FindControl<Border>("RightPane") is { } right && IsWithin(source, right))
                {
                    shell.FocusPane(right: true);
                }
                else if (this.FindControl<Border>("LeftPane") is { } left && IsWithin(source, left))
                {
                    shell.FocusPane(right: false);
                }
            },
            Avalonia.Interactivity.RoutingStrategies.Tunnel);

    /// <summary>
    /// Mirrors the browser-standard Alt+arrow navigation gestures. This is
    /// handled at the window tunnel because a hosted native web view may consume
    /// its key event before regular Avalonia key bindings can see it.
    /// </summary>
    private void OnNavigationShortcutTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 && e.KeyModifiers == KeyModifiers.None)
        {
            ToggleF11FullScreen();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && Shell is { IsOverflowOpen: true } overflowShell)
        {
            overflowShell.CloseOverflowCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && Shell is { IsDownloadsBubbleOpen: true } downloadsShell)
        {
            downloadsShell.CloseDownloadsBubbleCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            if (_suppressF11ExitOnEscape)
            {
                _suppressF11ExitOnEscape = false;
            }
            else if (_htmlFullScreen)
            {
                // YouTube (and other HTML fullscreen) owns this Escape.
            }
            else if (_f11FullScreen)
            {
                _f11FullScreen = false;
                ApplyBrowserFullScreen();
                e.Handled = true;
                return;
            }
        }

        // Handled here as well as in the window's KeyBindings because a hosted
        // native web view can consume the key before those are consulted.
        if (Shell is { } bookmarkShell)
        {
            if (e.Key == Key.D && e.KeyModifiers == KeyModifiers.Control)
            {
                bookmarkShell.BookmarkActiveTabCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.J && e.KeyModifiers == KeyModifiers.Control)
            {
                bookmarkShell.OpenDownloadsPageCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.H && e.KeyModifiers == KeyModifiers.Control)
            {
                bookmarkShell.OpenHistoryPageCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.T && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
            {
                bookmarkShell.ReopenClosedTabCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
            {
                bookmarkShell.FindInPageCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.P && e.KeyModifiers == KeyModifiers.Control)
            {
                bookmarkShell.PrintActiveCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.K && e.KeyModifiers == KeyModifiers.Control)
            {
                bookmarkShell.ToggleCommandPaletteCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.R && e.KeyModifiers == KeyModifiers.Control)
            {
                bookmarkShell.ReloadActiveCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.None)
            {
                bookmarkShell.OpenDevToolsCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.B && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
            {
                bookmarkShell.ToggleBookmarkBarCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.N && (e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)))
        {
            if (Shell?.WindowManager is WindowManager manager)
            {
                var window = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                    ? manager.CreatePrivateWindow()
                    : manager.CreateWindow();
                window.Show();
                e.Handled = true;
            }

            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.L)
        {
            this.FindControl<BrowserToolbarView>("BrowserToolbar")?.FocusAddress();
            e.Handled = true;
            return;
        }

        if ((e.KeyModifiers & KeyModifiers.Control) != 0 &&
            Shell?.FocusedBrowser is { } zoomBrowser)
        {
            var zoomCommand = e.Key switch
            {
                Key.OemPlus or Key.Add => zoomBrowser.ZoomInCommand,
                Key.OemMinus or Key.Subtract => zoomBrowser.ZoomOutCommand,
                Key.D0 or Key.NumPad0 => zoomBrowser.ResetZoomCommand,
                _ => null,
            };

            if (zoomCommand?.CanExecute(null) == true)
            {
                zoomCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        if (e.KeyModifiers != KeyModifiers.Alt || Shell?.FocusedBrowser is not { } browser)
        {
            return;
        }

        var command = e.Key switch
        {
            Key.Left => browser.GoBackCommand,
            Key.Right => browser.GoForwardCommand,
            _ => null,
        };

        if (command?.CanExecute(null) != true)
        {
            return;
        }

        command.Execute(null);
        e.Handled = true;
    }

    private void OnZoomWheelTunnel(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) == 0 ||
            Shell?.FocusedBrowser is not { } browser ||
            Math.Abs(e.Delta.Y) < double.Epsilon)
        {
            return;
        }

        var command = e.Delta.Y > 0 ? browser.ZoomInCommand : browser.ZoomOutCommand;
        command.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// Closes the "Other tabs" overflow drawer when a click lands outside it.
    /// The drawer had no dismiss path at all before this: it only closed when
    /// the toggle button was clicked again or a tab inside it was torn off, so
    /// clicking the page behind it, another tab, or anywhere else left it
    /// sitting open indefinitely. Wired as a tunnel handler, like HookPaneFocus
    /// above, because a click on the hosted native web view never bubbles back
    /// up to a normal handler.
    /// </summary>
    private void OnOverflowDismissTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (Shell is not { IsOverflowOpen: true } shell || e.Source is not Visual source)
        {
            return;
        }

        if (this.FindControl<Border>("OverflowDrawer") is { } drawer && IsWithin(source, drawer))
        {
            return;
        }

        if (this.FindControl<Button>("OverflowToggle") is { } toggle && IsWithin(source, toggle))
        {
            return;
        }

        shell.CloseOverflowCommand.Execute(null);
    }

    /// <summary>True when <paramref name="candidate"/> is, or sits inside, <paramref name="container"/>.</summary>
    private static bool IsWithin(Visual candidate, Visual container)
    {
        for (var v = candidate; v is not null; v = v.GetVisualParent())
        {
            if (ReferenceEquals(v, container))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Opens a tab in a window of its own, placed just off this one so it does not
    /// land exactly on top and look like nothing happened.
    /// </summary>
    private void MoveTabToNewWindow(TabItemViewModel tab)
    {
        if (Shell is not { } shell || shell.WindowManager is not WindowManager manager)
        {
            return;
        }

        if (!shell.TryExtractTransfer(tab, out var transfer, out _) || transfer is null)
        {
            return;
        }

        shell.CloseOverflowCommand.Execute(null);

        manager.TearOff(
            transfer,
            new PixelPoint(Position.X + 40, Position.Y + 40),
            new Size(Width, Height));
    }

    private void OnMinimizeRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_f11FullScreen || _htmlFullScreen)
        {
            return;
        }

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnHtmlFullScreenElementChanged(object? sender, EventArgs e)
    {
        var html = Shell?.HasHtmlFullScreenElement == true;

        if (_htmlFullScreen && !html)
        {
            _suppressF11ExitOnEscape = true;
        }

        _htmlFullScreen = html;
        ApplyBrowserFullScreen();
    }

    private void OnAcceleratorKeyPressed(object? sender, EngineAcceleratorKeyPressedEventArgs e)
    {
        if (!e.IsKeyDown)
        {
            return;
        }

        const int virtualKeyF11 = 0x7A;
        const int virtualKeyEscape = 0x1B;

        if (e.VirtualKey == virtualKeyF11)
        {
            e.Handled = true;
            Dispatcher.UIThread.Post(ToggleF11FullScreen);
            return;
        }

        if (e.VirtualKey != virtualKeyEscape)
        {
            return;
        }

        if (Shell is { IsOverflowOpen: true })
        {
            e.Handled = true;
            Dispatcher.UIThread.Post(() => Shell?.CloseOverflowCommand.Execute(null));
            return;
        }

        if (Shell is { IsDownloadsBubbleOpen: true })
        {
            e.Handled = true;
            Dispatcher.UIThread.Post(() => Shell?.CloseDownloadsBubbleCommand.Execute(null));
            return;
        }

        if (_htmlFullScreen)
        {
            // YouTube (and other HTML fullscreen) owns this Escape.
            return;
        }

        if (_f11FullScreen)
        {
            e.Handled = true;
            Dispatcher.UIThread.Post(() =>
            {
                _f11FullScreen = false;
                ApplyBrowserFullScreen();
            });
        }
    }

    private void ToggleF11FullScreen()
    {
        _f11FullScreen = !_f11FullScreen;
        ApplyBrowserFullScreen();
    }

    /// <summary>
    /// F11 and HTML fullscreen share one chrome-hidden window state. Leaving
    /// YouTube must not drop an F11 lock, and leaving F11 must not interrupt a
    /// video that is still in the Fullscreen API.
    /// </summary>
    private void ApplyBrowserFullScreen()
    {
        if (_applyingFullScreen)
        {
            return;
        }

        _applyingFullScreen = true;

        try
        {
            var hideChrome = _f11FullScreen || _htmlFullScreen;

            if (Shell is { } shell)
            {
                shell.IsBrowserFullScreen = hideChrome;

                if (hideChrome)
                {
                    if (shell.IsOverflowOpen)
                    {
                        shell.CloseOverflowCommand.Execute(null);
                    }

                    shell.IsDownloadsBubbleOpen = false;
                }
            }

            ExtendClientAreaTitleBarHeightHint = hideChrome ? 0 : -1;

            if (hideChrome)
            {
                if (WindowState != WindowState.FullScreen)
                {
                    _restoreWindowState = WindowState;
                    WindowState = WindowState.FullScreen;
                }
            }
            else if (WindowState == WindowState.FullScreen)
            {
                WindowState = _restoreWindowState == WindowState.FullScreen
                    ? WindowState.Normal
                    : _restoreWindowState;
            }
        }
        finally
        {
            _applyingFullScreen = false;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != WindowStateProperty || _applyingFullScreen)
        {
            return;
        }

        // Never force fullscreen back on. Doing that kept chrome hidden after
        // YouTube left HTML fullscreen while the window had been maximised:
        // the engine restored Maximized first, then this handler saw the HTML
        // flag still set and put the window into FullScreen again.
        if (WindowState != WindowState.FullScreen && (_f11FullScreen || _htmlFullScreen || Shell?.IsBrowserFullScreen == true))
        {
            _f11FullScreen = false;
            _htmlFullScreen = false;

            if (Shell is { } shell)
            {
                shell.IsBrowserFullScreen = false;
            }

            ExtendClientAreaTitleBarHeightHint = -1;
        }
    }

    private void OnCloseRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnPointerReleasedTunnel(object? sender, PointerReleasedEventArgs e)
    {
        if (Shell is not { } shell || e.Source is not Visual source)
        {
            return;
        }

        // A drag that just ended reordered the strip; it must not also switch tabs.
        if (_tabDrag?.IsDragging == true)
        {
            return;
        }

        // Only the left button activates. A middle-click has already closed the
        // tab by the time this runs, and activating a closed tab is meaningless.
        if (e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        if (source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        // Only a title-bar tab may use this window-level activation path. Popup
        // rows also carry TabItemViewModel as their DataContext; treating those
        // borders as strip headers activates a split candidate before its picker
        // command runs and closes Other Tabs before its drag can complete.
        Border? header = null;

        for (Visual? visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Border candidate && candidate.Classes.Contains("tab"))
            {
                header = candidate;
                break;
            }
        }

        if (header?.DataContext is TabItemViewModel tab)
        {
            shell.ActivateTabCommand.Execute(tab);
        }
    }

    /// <summary>
    /// True when a screen point falls inside this window's tab strip, which is how
    /// a released drag decides whether it landed on another window.
    /// </summary>
    public bool TabStripContainsScreenPoint(PixelPoint screenPoint)
    {
        if (this.FindControl<ItemsControl>("TabStrip") is not { } strip ||
            strip.TranslatePoint(default, this) is not { } origin)
        {
            return false;
        }

        var local = this.PointToClient(screenPoint);

        return local.X >= origin.X && local.X <= origin.X + strip.Bounds.Width &&
               local.Y >= origin.Y && local.Y <= origin.Y + strip.Bounds.Height;
    }

    /// <summary>
    /// The visible tab that should follow an entry dropped at this screen point.
    /// Identity remains correct when the strip omits overflow, collapsed group
    /// members and hidden split partners; a visual integer index does not.
    /// </summary>
    public TabItemViewModel? DropBeforeForScreenPoint(PixelPoint screenPoint)
    {
        if (this.FindControl<ItemsControl>("TabStrip") is not { } strip)
        {
            return null;
        }

        var local = this.PointToClient(screenPoint);

        foreach (var container in strip.GetRealizedContainers())
        {
            if (container.DataContext is not TabItemViewModel tab)
            {
                continue;
            }

            if (container.TranslatePoint(default, this) is { } origin &&
                local.X < origin.X + container.Bounds.Width / 2)
            {
                return tab;
            }
        }

        return null;
    }

    /// <summary>
    /// The group a tab dropped at this screen point would join: the group of the
    /// tab or chip directly under the pointer. A collapsed group refuses, since the
    /// tab would vanish into it.
    /// </summary>
    public Domain.ValueObjects.TabGroupId? GroupHintForScreenPoint(PixelPoint screenPoint)
    {
        if (this.FindControl<ItemsControl>("TabStrip") is not { } strip)
        {
            return null;
        }

        var local = this.PointToClient(screenPoint);

        foreach (var container in strip.GetRealizedContainers())
        {
            if (container.TranslatePoint(default, this) is not { } origin ||
                local.X < origin.X ||
                local.X >= origin.X + container.Bounds.Width)
            {
                continue;
            }

            return container.DataContext switch
            {
                TabItemViewModel tab => tab.Tab.GroupId,
                GroupHeaderViewModel { IsCollapsed: false } chip => chip.Id,
                _ => null,
            };
        }

        return null;
    }

    /// <summary>Shows the insertion preview where a dropped tab would land.</summary>
    public void ShowDropIndicatorAt(PixelPoint screenPoint)
    {
        if (this.FindControl<ItemsControl>("TabStrip") is not { } strip ||
            this.FindControl<Border>("DropIndicator") is not { } indicator ||
            indicator.Parent is not Visual host)
        {
            return;
        }

        var local = this.PointToClient(screenPoint);
        double x = 0;

        foreach (var container in strip.GetRealizedContainers())
        {
            if (container.DataContext is not TabItemViewModel)
            {
                continue;
            }

            if (container.TranslatePoint(default, this) is not { } origin)
            {
                continue;
            }

            if (local.X < origin.X + container.Bounds.Width / 2)
            {
                x = origin.X;
                break;
            }

            x = origin.X + container.Bounds.Width;
        }

        if (host.TranslatePoint(default, this) is { } hostOrigin)
        {
            x -= hostOrigin.X;
        }

        indicator.RenderTransform = new TranslateTransform(Math.Max(0, x - 1), 0);
        indicator.IsVisible = true;
    }

    public void HideDropIndicator()
    {
        if (this.FindControl<Border>("DropIndicator") is { } indicator)
        {
            indicator.IsVisible = false;
        }
    }
}
