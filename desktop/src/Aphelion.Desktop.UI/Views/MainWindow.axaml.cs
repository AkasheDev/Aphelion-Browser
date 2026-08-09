using Aphelion.Desktop.UI.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Aphelion.Desktop.UI.Views;

public partial class MainWindow : Window
{
    private TabStripDragHandler? _tabDrag;

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
    }

    private ShellViewModel? Shell => (DataContext as MainWindowViewModel)?.Shell;

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

    /// <summary>The index a tab dropped at this screen point should take.</summary>
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
            if (container is Visual visual &&
                visual.TranslatePoint(default, this) is { } origin &&
                local.X < origin.X + visual.Bounds.Width / 2)
            {
                return index;
            }

            index++;
        }

        return index;
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

        if (source is Button || source.FindAncestorOfType<Button>() is not null)
        {
            return;
        }

        var header = source as Border ?? source.FindAncestorOfType<Border>();

        if (header?.DataContext is TabItemViewModel tab)
        {
            shell.ActivateTabCommand.Execute(tab);
        }
    }
}
