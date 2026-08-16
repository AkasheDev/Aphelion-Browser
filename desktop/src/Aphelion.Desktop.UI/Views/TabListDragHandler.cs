using Aphelion.Desktop.UI.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Aphelion.Desktop.UI.Views;

/// <summary>
/// Dragging a tab in the overflow list: reorder it in the list, put it on a tab
/// strip, move it to another window, or tear it into a window of its own.
/// </summary>
/// <remarks>
/// The pointer is captured on the stable list root, so the drag keeps reporting
/// after it leaves the panel and survives a row collection reconciliation.
/// </remarks>
internal sealed class TabListDragHandler
{
    /// <summary>Pointer travel before a press becomes a drag, in pixels.</summary>
    private const double DragThreshold = 5;

    private readonly Control _root;
    private readonly Func<TabListViewModel?> _list;

    private TabItemViewModel? _dragging;
    private Control? _row;
    private Point _pressOrigin;
    private bool _isDragging;
    private IPointer? _capturedPointer;

    public TabListDragHandler(Control root, Func<TabListViewModel?> list)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _list = list ?? throw new ArgumentNullException(nameof(list));

        _root.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _root.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _root.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _root.AddHandler(InputElement.KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private ShellViewModel? Shell => _list()?.Owner;

    private WindowManager? Manager => Shell?.WindowManager as WindowManager;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragging = null;
        _row = null;
        _isDragging = false;

        if (!e.GetCurrentPoint(_root).Properties.IsLeftButtonPressed ||
            e.Source is not Visual source)
        {
            return;
        }

        // A close/menu button inside a row owns its gesture; only the row surface
        // begins a drag.
        if (source.FindAncestorOfType<Button>(includeSelf: true) is not null ||
            RowFrom(source) is not { } row ||
            row.DataContext is not TabItemViewModel tab)
        {
            return;
        }

        _dragging = tab;
        _row = row;
        _pressOrigin = e.GetPosition(_root);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null || Shell is null)
        {
            return;
        }

        var position = e.GetPosition(_root);

        if (!_isDragging)
        {
            if (Math.Abs(position.X - _pressOrigin.X) < DragThreshold &&
                Math.Abs(position.Y - _pressOrigin.Y) < DragThreshold)
            {
                return;
            }

            _isDragging = true;

            // Without the capture the drag would end at the panel's edge, which is
            // the one place it must survive.
            e.Pointer.Capture(_root);
            _capturedPointer = e.Pointer;
        }

        var outside = !IsInsidePanel(position);

        if (_row is not null)
        {
            _row.Opacity = outside ? 0.5 : 1.0;
        }

        // Inside the panel nothing is being aimed at yet, so no preview is shown.
        if (Manager is { } manager && TopLevel.GetTopLevel(_root) is MainWindow owner)
        {
            if (outside)
            {
                manager.UpdateDropPreview(
                    owner.PointToScreen(e.GetPosition(owner)),
                    exclude: null,
                    sourceIsPrivate: Shell?.IsPrivateWindow == true);
            }
            else
            {
                manager.ClearDropPreview();
            }
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pressed = _dragging;
        var pressedRow = _row;
        var wasDragging = _isDragging;
        var position = e.GetPosition(_root);
        var releasedRow = RowFrom(e.Source as Visual);
        var releasedOverButton = e.Source is Visual source &&
                                 source.FindAncestorOfType<Button>(includeSelf: true) is not null;

        if (pressed is null)
        {
            return;
        }

        Reset();

        if (!wasDragging)
        {
            if (!releasedOverButton && ReferenceEquals(releasedRow, pressedRow))
            {
                _list()?.ChooseCommand.Execute(pressed);
                e.Handled = true;
            }

            return;
        }

        // Dragging and choosing are mutually exclusive outcomes.
        e.Handled = true;

        // Inside the panel, vertical placement changes the real session order.
        // The identity-based domain move keeps this correct even though only the
        // overflow subset is currently rendered.
        if (IsInsidePanel(position))
        {
            if (Shell is { } localShell)
            {
                var before = DropBefore(position, pressed);
                localShell.ReorderTab(pressed, before);
            }

            return;
        }

        if (Shell is not { } shell ||
            Manager is not { } manager ||
            TopLevel.GetTopLevel(_root) is not MainWindow owner)
        {
            return;
        }

        var screenPoint = owner.PointToScreen(e.GetPosition(owner));

        // The tab left the list one way or another, so the panel has served its
        // purpose — closing it also reveals where the tab went.
        shell.CloseOverflowCommand.Execute(null);

        TabTransfer.Complete(shell, manager, owner, pressed, screenPoint, allowSameWindow: true);
    }

    /// <summary>Escape abandons the drag, leaving the tab where it was.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isDragging)
        {
            Reset();
            e.Handled = true;
        }
    }

    private void Reset()
    {
        if (_row is not null)
        {
            _row.ClearValue(Visual.OpacityProperty);
        }

        _capturedPointer?.Capture(null);
        _capturedPointer = null;
        Manager?.ClearDropPreview();

        _dragging = null;
        _row = null;
        _isDragging = false;
    }

    /// <summary>Whether a point in the list's own space still falls within it.</summary>
    private bool IsInsidePanel(Point position) =>
        position.X >= 0 && position.X <= _root.Bounds.Width &&
        position.Y >= 0 && position.Y <= _root.Bounds.Height;

    private static Border? RowFrom(Visual? source)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Border row && row.Classes.Contains("panel-row"))
            {
                return row;
            }
        }

        return null;
    }

    private TabItemViewModel? DropBefore(Point position, TabItemViewModel dragged)
    {
        foreach (var row in _root.GetVisualDescendants()
                     .OfType<Border>()
                     .Where(row => row.Classes.Contains("panel-row") &&
                                   row.DataContext is TabItemViewModel &&
                                   !ReferenceEquals(row.DataContext, dragged))
                     .OrderBy(row => row.TranslatePoint(default, _root)?.Y ?? double.MaxValue))
        {
            if (row.TranslatePoint(default, _root) is { } origin &&
                position.Y < origin.Y + row.Bounds.Height / 2)
            {
                return (TabItemViewModel)row.DataContext!;
            }
        }

        return _list()?.ItemAfterCurrentPage;
    }
}
