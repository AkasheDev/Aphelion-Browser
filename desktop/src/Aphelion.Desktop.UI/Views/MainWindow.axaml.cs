using Aphelion.Desktop.UI.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Aphelion.Desktop.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Dragging, double-click maximise, snap and the caption buttons are handled
        // by the window decoration roles declared in XAML. Only tab activation is
        // ours, and it is tunnelled because a tab header is a Border rather than a
        // Button — a Button would swallow the drag gesture the title bar needs.
        AddHandler(PointerPressedEvent, OnPointerPressedTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnMinimizeRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { Shell: { } shell } ||
            e.Source is not Visual source)
        {
            return;
        }

        // A click on the close button is that button's business, not a tab switch.
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
