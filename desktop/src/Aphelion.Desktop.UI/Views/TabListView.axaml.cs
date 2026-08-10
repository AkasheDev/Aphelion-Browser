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

    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) ||
            (e.Source is Visual source &&
             source.FindAncestorOfType<Button>(includeSelf: true) is not null) ||
            sender is not Border { DataContext: TabItemViewModel tab } ||
            DataContext is not TabListViewModel list)
        {
            return;
        }

        list.ChooseCommand.Execute(tab);
        e.Handled = true;
    }

    private void OnCloseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TabItemViewModel tab } &&
            DataContext is TabListViewModel { CanClose: true } list)
        {
            list.CloseCommand.Execute(tab);
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

        Border? row = null;

        for (Visual? visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Border candidate && candidate.Classes.Contains("panel-row"))
            {
                row = candidate;
                break;
            }
        }

        if (row?.DataContext is TabItemViewModel tab)
        {
            list.CloseCommand.Execute(tab);
            e.Handled = true;
        }
    }
}
