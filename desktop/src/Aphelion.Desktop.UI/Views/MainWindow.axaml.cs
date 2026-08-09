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

        // The strip panel measures how many tabs fit and reports it here; the
        // shell lists the remainder. Routed while this window is active so the
        // report reaches the right shell when several windows are open.
        Activated += (_, _) => Controls.TabStripPanel.CapacityReporter =
            capacity => Shell?.ReportStripCapacity(capacity);

        // Tab activation is handled here rather than with a per-tab command: a tab
        // header is a Border, not a Button, because a Button would swallow the
        // press the drag handler needs.
        AddHandler(PointerReleasedEvent, OnPointerReleasedTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private ShellViewModel? Shell => (DataContext as MainWindowViewModel)?.Shell;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Activated does not always fire for the first window on startup.
        Controls.TabStripPanel.CapacityReporter =
            capacity => Shell?.ReportStripCapacity(capacity);
    }

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
    /// Gives the partner's half of a tab an equal share of the width, and none at
    /// all when the tab is not split. A star column keeps its share even when its
    /// content is collapsed, which would leave a gap beside the first title.
    /// </summary>
    /// <summary>
    /// Applies the split layout whenever the tab's split state arrives or
    /// changes. Tag carries IsSplit purely so this fires; DataContext covers the
    /// container being recycled onto a different tab.
    /// </summary>
    private void OnTabHalvesPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TagProperty && e.Property != DataContextProperty)
        {
            return;
        }

        if (sender is not Grid { ColumnDefinitions.Count: 3 } halves)
        {
            return;
        }

        halves.ColumnDefinitions[2].Width = halves.DataContext is TabItemViewModel { IsSplit: true }
            ? new GridLength(1, GridUnitType.Star)
            : GridLength.Auto;
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
