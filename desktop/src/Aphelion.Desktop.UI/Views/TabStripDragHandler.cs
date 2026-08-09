using Aphelion.Desktop.UI.ViewModels;
using Aphelion.Desktop.UI;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Aphelion.Desktop.UI.Views;

/// <summary>
/// Chrome-style dragging for the tab strip: tabs reorder live as the pointer
/// moves, displaced tabs slide into their new places, and dropping among a
/// group's members joins that group.
/// </summary>
/// <remarks>
/// The animation works by compensation. Reordering the collection moves a tab
/// instantly; immediately afterwards each tab is offset back to where it just was
/// and then released, so the styled transition carries it to its new position.
/// The dragged tab is exempt — it tracks the pointer directly, without easing.
/// </remarks>
internal sealed class TabStripDragHandler
{
    /// <summary>Pointer travel before a press becomes a drag, in pixels.</summary>
    private const double DragThreshold = 5;

    /// <summary>
    /// How far outside the strip the pointer must go before a release tears the tab
    /// off. A margin stops a slightly sloppy drop from spawning a window.
    /// </summary>
    private const double TearOffMargin = 24;

    private readonly ItemsControl _strip;
    private readonly Func<ShellViewModel?> _shell;

    private TabItemViewModel? _dragging;
    private Point _pressOrigin;
    private bool _isDragging;

    /// <summary>Where the dragged tab sat when the drag began, in strip space.</summary>
    private double _dragOriginX;

    public TabStripDragHandler(ItemsControl strip, Func<ShellViewModel?> shell)
    {
        _strip = strip ?? throw new ArgumentNullException(nameof(strip));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));

        _strip.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _strip.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _strip.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _strip.AddHandler(InputElement.KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    public bool IsDragging => _isDragging;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_strip).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // The close button owns its own clicks.
        if (e.Source is Visual source && source.FindAncestorOfType<Button>() is not null)
        {
            return;
        }

        _dragging = TabUnder(e.Source as Visual);
        _pressOrigin = e.GetPosition(_strip);
        _isDragging = false;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null || _shell() is not { } shell)
        {
            return;
        }

        var position = e.GetPosition(_strip);

        if (!_isDragging)
        {
            if (Math.Abs(position.X - _pressOrigin.X) < DragThreshold &&
                Math.Abs(position.Y - _pressOrigin.Y) < DragThreshold)
            {
                return;
            }

            _isDragging = true;
            _dragOriginX = LeftOf(_dragging) ?? position.X;
            SetDraggingClass(_dragging, true);
        }

        var target = IndexAt(position.X);
        var current = shell.Tabs.IndexOf(_dragging);

        if (target >= 0 && target != current)
        {
            var before = CaptureLefts();

            shell.DropTab(_dragging, target);

            // Layout has to settle before the new positions can be measured.
            _strip.UpdateLayout();
            AnimateFrom(before);

            // The dragged tab now sits in a new slot. Rebase the press origin onto
            // it so the offset below stays relative to the tab's current home
            // rather than the one it started in.
            if (LeftOf(_dragging) is { } left)
            {
                _pressOrigin = _pressOrigin.WithX(_pressOrigin.X + (left - _dragOriginX));
                _dragOriginX = left;
            }
        }

        // The dragged tab follows the pointer exactly rather than snapping to its
        // slot, so the cursor stays on the same part of the tab throughout.
        Offset(_dragging, position.X - _pressOrigin.X);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var dragged = _dragging;
        var wasDragging = _isDragging;

        if (dragged is not null)
        {
            SetDraggingClass(dragged, false);
            Offset(dragged, 0);
        }

        _dragging = null;
        _isDragging = false;

        if (wasDragging && dragged is not null)
        {
            CompleteAcrossWindows(dragged, e);
        }
    }

    /// <summary>
    /// Handles a release that left this window's tab strip: the tab either joins
    /// another window's strip or becomes a window of its own.
    /// </summary>
    private void CompleteAcrossWindows(TabItemViewModel dragged, PointerReleasedEventArgs e)
    {
        if (_shell() is not { } shell ||
            shell.WindowManager is not WindowManager manager ||
            TopLevel.GetTopLevel(_strip) is not MainWindow owner)
        {
            return;
        }

        var local = e.GetPosition(_strip);

        // Still inside the strip: the live reorder already placed it.
        if (local.Y >= -TearOffMargin &&
            local.Y <= _strip.Bounds.Height + TearOffMargin &&
            local.X >= -TearOffMargin &&
            local.X <= _strip.Bounds.Width + TearOffMargin)
        {
            return;
        }

        var screenPoint = owner.PointToScreen(e.GetPosition(owner));
        var address = ShellViewModel.AddressOf(dragged);

        if (manager.WindowAcceptingDropAt(screenPoint, owner) is { } target)
        {
            var index = target.DropIndexForScreenPoint(screenPoint);
            shell.DetachTab(dragged);
            AdoptInto(target, address, index);

            if (shell.Tabs.Count == 0)
            {
                owner.Close();
            }

            target.Activate();
            return;
        }

        // A window with a single tab is already its own window; tearing it off
        // would close this one and open an identical replacement.
        if (shell.IsSingleTab)
        {
            return;
        }

        shell.DetachTab(dragged);

        manager.TearOff(
            address,
            new PixelPoint(screenPoint.X - 120, screenPoint.Y - 20),
            new Size(owner.Width, owner.Height));
    }

    private static void AdoptInto(MainWindow target, Domain.ValueObjects.PageAddress? address, int index)
    {
        if (target.DataContext is MainWindowViewModel { Shell: { } shell })
        {
            shell.AdoptTab(address, index);
        }
    }

    /// <summary>Escape abandons the drag, leaving the strip as it stands.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_isDragging)
        {
            return;
        }

        if (_dragging is not null)
        {
            SetDraggingClass(_dragging, false);
            Offset(_dragging, 0);
        }

        _dragging = null;
        _isDragging = false;
        e.Handled = true;
    }

    /// <summary>Records where every tab currently sits, keyed by its view model.</summary>
    private Dictionary<object, double> CaptureLefts()
    {
        var positions = new Dictionary<object, double>();

        foreach (var container in _strip.GetRealizedContainers())
        {
            if (container is Visual visual &&
                container.DataContext is { } key &&
                visual.TranslatePoint(default, _strip) is { } origin)
            {
                positions[key] = origin.X;
            }
        }

        return positions;
    }

    /// <summary>
    /// Offsets each tab back to where it was, then releases it so the styled
    /// transition slides it to its new place.
    /// </summary>
    private void AnimateFrom(Dictionary<object, double> before)
    {
        foreach (var container in _strip.GetRealizedContainers())
        {
            if (container is not Border border ||
                container.DataContext is not { } key ||
                !before.TryGetValue(key, out var previous))
            {
                continue;
            }

            if (ReferenceEquals(key, _dragging))
            {
                continue;
            }

            if (border.TranslatePoint(default, _strip) is not { } origin)
            {
                continue;
            }

            var delta = previous - origin.X;

            if (Math.Abs(delta) < 0.5)
            {
                continue;
            }

            // Jump back without easing, then let the transition carry it forward.
            border.Transitions = null;
            border.RenderTransform = new TranslateTransform(delta, 0);
            border.Transitions = TabTransitions();
            border.RenderTransform = null;
        }
    }

    private static Transitions TabTransitions() =>
    [
        new TransformOperationsTransition
        {
            Property = Visual.RenderTransformProperty,
            Duration = TimeSpan.FromMilliseconds(160),
            Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
        },
    ];

    private void Offset(TabItemViewModel tab, double x)
    {
        if (ContainerFor(tab) is not { } border)
        {
            return;
        }

        border.RenderTransform = Math.Abs(x) < 0.5 ? null : new TranslateTransform(x, 0);
    }

    private void SetDraggingClass(TabItemViewModel tab, bool dragging)
    {
        if (ContainerFor(tab) is not { } border)
        {
            return;
        }

        if (dragging)
        {
            border.Transitions = null;
            border.Classes.Add("dragging");
        }
        else
        {
            border.Classes.Remove("dragging");
            border.Transitions = TabTransitions();
        }
    }

    private double? LeftOf(TabItemViewModel tab) =>
        ContainerFor(tab)?.TranslatePoint(default, _strip)?.X;

    private Border? ContainerFor(TabItemViewModel tab)
    {
        foreach (var container in _strip.GetRealizedContainers())
        {
            if (container is Border border && ReferenceEquals(container.DataContext, tab))
            {
                return border;
            }
        }

        return null;
    }

    private static TabItemViewModel? TabUnder(Visual? visual)
    {
        var border = visual as Border ?? visual?.FindAncestorOfType<Border>();
        return border?.DataContext as TabItemViewModel;
    }

    /// <summary>
    /// The index the dragged tab should occupy for a pointer at <paramref name="x"/>.
    /// A tab is displaced once the pointer passes its midpoint, so tabs swap under
    /// the cursor rather than only when it reaches their far edge.
    /// </summary>
    private int IndexAt(double x)
    {
        var index = 0;

        foreach (var container in _strip.GetRealizedContainers())
        {
            if (container is not Visual visual ||
                visual.TranslatePoint(default, _strip) is not { } origin)
            {
                index++;
                continue;
            }

            if (x < origin.X + visual.Bounds.Width / 2)
            {
                return index;
            }

            index++;
        }

        return Math.Max(0, index - 1);
    }
}
