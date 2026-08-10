using Aphelion.Desktop.UI.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Aphelion.Desktop.UI.Views;

/// <summary>
/// Dragging a tab out of the overflow list: onto this window's strip to bring it
/// back within reach, onto another window's strip, or onto empty desktop to give
/// it a window of its own.
/// </summary>
/// <remarks>
/// The pointer is captured on the row, so the drag keeps reporting after it leaves
/// the panel — without that, the moment the pointer crossed the panel's edge the
/// events would stop and the drop could never be placed.
/// <para>
/// The list does not reorder. Rows are only dragged out of it; where a tab belongs
/// in the order is decided on the strip, where the order is actually visible.
/// </para>
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

    public TabListDragHandler(Control root, Func<TabListViewModel?> list)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _list = list ?? throw new ArgumentNullException(nameof(list));

        _root.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _root.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _root.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _root.AddHandler(InputElement.KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>True while a row is actually being dragged, not merely pressed.</summary>
    public bool IsDragging => _isDragging;

    private ShellViewModel? Shell => _list()?.Owner;

    private WindowManager? Manager => Shell?.WindowManager as WindowManager;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragging = null;
        _row = null;
        _isDragging = false;

        if (!e.GetCurrentPoint(_root).Properties.IsLeftButtonPressed ||
            Shell is null ||
            e.Source is not Visual source)
        {
            return;
        }

        // The row is itself a Button, so the usual "ignore presses on buttons"
        // rule cannot apply. The nearest button is either the row or something
        // inside it — the close button — and only the row is draggable.
        if (source.FindAncestorOfType<Button>(includeSelf: true) is not { } button ||
            !button.Classes.Contains("panel-row") ||
            button.DataContext is not TabItemViewModel tab)
        {
            return;
        }

        _dragging = tab;
        _row = button;
        _pressOrigin = e.GetPosition(_root);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null)
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
            e.Pointer.Capture(_row);
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
                manager.UpdateDropPreview(owner.PointToScreen(e.GetPosition(owner)), exclude: null);
            }
            else
            {
                manager.ClearDropPreview();
            }
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var dragged = _dragging;
        var wasDragging = _isDragging;
        var position = e.GetPosition(_root);

        Reset(e.Pointer);

        if (dragged is null || !wasDragging)
        {
            return;
        }

        // A drag that ends is never also a click: the row underneath the pointer
        // must not be activated because the tab was let go over it.
        e.Handled = true;

        // Released without ever leaving the panel: the user changed their mind, or
        // simply pressed a little unsteadily. Nothing moves.
        if (IsInsidePanel(position))
        {
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

        TabTransfer.Complete(shell, manager, owner, dragged, screenPoint, allowSameWindow: true);
    }

    /// <summary>Escape abandons the drag, leaving the tab where it was.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isDragging)
        {
            Reset(pointer: null);
            e.Handled = true;
        }
    }

    private void Reset(IPointer? pointer)
    {
        if (_row is not null)
        {
            _row.Opacity = 1.0;
        }

        pointer?.Capture(null);
        Manager?.ClearDropPreview();

        _dragging = null;
        _row = null;
        _isDragging = false;
    }

    /// <summary>Whether a point in the list's own space still falls within it.</summary>
    private bool IsInsidePanel(Point position) =>
        position.X >= 0 && position.X <= _root.Bounds.Width &&
        position.Y >= 0 && position.Y <= _root.Bounds.Height;
}
