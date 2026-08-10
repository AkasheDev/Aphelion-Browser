using Aphelion.Desktop.UI.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Aphelion.Desktop.UI.Views;

public partial class TabListView : UserControl
{
    private readonly TabListDragHandler _drag;

    public TabListView()
    {
        InitializeComponent();

        _drag = new TabListDragHandler(this, () => DataContext as TabListViewModel);

        // Middle-click closes a row, as it does in the strip.
        AddHandler(PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // A list that only picks — the split picker — offers no tab management, so
        // its rows have no menu. The menu is defined once in the template and
        // suppressed here rather than built twice.
        AddHandler(ContextRequestedEvent, OnContextRequested, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not TabListViewModel { CanManage: true })
        {
            e.Handled = true;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed ||
            DataContext is not TabListViewModel { CanClose: true } list ||
            e.Source is not Visual source)
        {
            return;
        }

        // Rows are Buttons carrying the tab as their DataContext.
        var row = source.FindAncestorOfType<Button>(includeSelf: true);

        if (row?.DataContext is TabItemViewModel tab)
        {
            list.CloseCommand.Execute(tab);
            e.Handled = true;
        }
    }
}
