using System.ComponentModel;
using Aphelion.Desktop.UI.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Aphelion.Desktop.UI.Views;

public partial class MainWindow : Window
{
    private TabStripDragHandler? _tabDrag;
    private ShellViewModel? _hookedShell;

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
        HookPaneFocus();
    }

    private ShellViewModel? Shell => (DataContext as MainWindowViewModel)?.Shell;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_hookedShell is not null)
        {
            _hookedShell.PropertyChanged -= OnShellPropertyChanged;
        }

        _hookedShell = Shell;

        if (_hookedShell is not null)
        {
            _hookedShell.PropertyChanged += OnShellPropertyChanged;

            // "Move to new window" needs the window manager and this window's
            // geometry, so the window supplies it rather than the view model.
            _hookedShell.MoveToNewWindow = MoveTabToNewWindow;
        }

        UpdateSplitLayout();
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsSplit))
        {
            UpdateSplitLayout();
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

        host.ColumnDefinitions[2].Width = Shell?.IsSplit == true
            ? new GridLength(1, GridUnitType.Star)
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

        var address = ShellViewModel.AddressOf(tab);
        shell.DetachTab(tab);
        shell.CloseOverflowCommand.Execute(null);

        manager.TearOff(
            address,
            new PixelPoint(Position.X + 40, Position.Y + 40),
            new Size(Width, Height));
    }

    /// <summary>Clicking the backdrop dismisses whichever panel is open.</summary>
    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only the backdrop itself; a click inside the panel must not close it.
        if (!ReferenceEquals(e.Source, sender) || Shell is not { } shell)
        {
            return;
        }

        shell.CloseOverflowCommand.Execute(null);
        shell.CloseSplitPickerCommand.Execute(null);
        e.Handled = true;
    }

    private void OnMinimizeRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

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

        var header = source as Border ?? source.FindAncestorOfType<Border>();

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
    /// The session index a tab dropped at this screen point should take. Group
    /// chips occupy strip space but no session index, so only tabs are counted.
    /// </summary>
    public int DropIndexForScreenPoint(PixelPoint screenPoint)
    {
        if (this.FindControl<ItemsControl>("TabStrip") is not { } strip)
        {
            return 0;
        }

        var local = this.PointToClient(screenPoint);
        var index = 0;

        foreach (var container in strip.GetRealizedContainers())
        {
            if (container.DataContext is not TabItemViewModel)
            {
                continue;
            }

            if (container.TranslatePoint(default, this) is { } origin &&
                local.X < origin.X + container.Bounds.Width / 2)
            {
                return index;
            }

            index++;
        }

        return index;
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
