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

        // The window has no system chrome, so dragging and the click targets on
        // tabs have to be wired by hand.
        var titleBar = this.FindControl<Grid>("TitleBar");

        if (titleBar is not null)
        {
            titleBar.PointerPressed += OnTitleBarPointerPressed;
        }

        AddHandler(PointerPressedEvent, OnAnyPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Buttons and tabs handle their own clicks; the window must neither move nor
        // maximise when one of them is pressed.
        if (IsInteractive(e.Source as Visual))
        {
            return;
        }

        // Double click on empty title-bar space toggles maximise, as elsewhere.
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        BeginMoveDrag(e);
    }

    /// <summary>
    /// Activates the tab whose header was clicked. Handled here rather than with a
    /// per-tab command because the tab header is a Border, not a Button — a Button
    /// would swallow the drag gesture the title bar needs.
    /// </summary>
    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { Shell: { } shell })
        {
            return;
        }

        if (e.Source is not Visual source)
        {
            return;
        }

        // A click on the close button is that button's business, not a tab switch.
        if (IsButton(source))
        {
            return;
        }

        var header = source as Border ?? source.FindAncestorOfType<Border>();

        if (header?.DataContext is TabItemViewModel tab)
        {
            shell.ActivateTabCommand.Execute(tab);
        }
    }

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    /// <summary>
    /// True when the visual is a button or sits inside one. FindAncestorOfType
    /// excludes the element itself, so a click landing directly on a Button would
    /// otherwise read as a click on empty chrome.
    /// </summary>
    private static bool IsButton(Visual? visual) =>
        visual is Button || visual?.FindAncestorOfType<Button>() is not null;

    /// <summary>True for anything in the title bar that owns its own click.</summary>
    private static bool IsInteractive(Visual? visual)
    {
        if (visual is null || IsButton(visual))
        {
            return IsButton(visual);
        }

        var border = visual as Border ?? visual.FindAncestorOfType<Border>();
        return border?.DataContext is TabItemViewModel;
    }
}
