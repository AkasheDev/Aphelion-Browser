using System.Globalization;
using Aphelion.Desktop.UI.ViewModels;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;

namespace Aphelion.Desktop.UI.Views;

/// <summary>
/// Chrome-style dragging for the tab strip: tabs reorder live as the pointer
/// moves, displaced tabs slide into their new places, dropping among a group's
/// members joins that group, and a drag released outside the strip tears the tab
/// into its own window or drops it onto another window's strip.
/// </summary>
/// <remarks>
/// Transforms are applied to the item containers the ItemsControl creates, not to
/// the templated Border inside them — an earlier version targeted the Border and
/// no transform ever landed. The slide works by compensation: reordering the
/// collection moves a container instantly, so each displaced container is offset
/// back to where it just was and released, letting a transition carry it forward.
/// The dragged tab is exempt and tracks the pointer directly.
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

    private static readonly TransformOperations NoOffset = TransformOperations.Parse("translate(0px, 0px)");

    private readonly ItemsControl _strip;
    private readonly Func<ShellViewModel?> _shell;

    private TabItemViewModel? _dragging;
    private Point _pressOrigin;
    private bool _isDragging;

    /// <summary>Where the dragged tab's container sat when the drag began, in strip space.</summary>
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

    private WindowManager? Manager => _shell()?.WindowManager as WindowManager;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_strip).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Buttons — the close button, the group chips — own their own clicks.
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
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

            if (ContainerFor(_dragging) is { } lifted)
            {
                // Above its neighbours, and no easing: an eased dragged tab lags
                // behind the cursor.
                lifted.ZIndex = 10;
                lifted.Transitions = null;
            }
        }

        var target = TabIndexAt(position.X);
        var current = shell.IndexOfTab(_dragging);

        if (target >= 0 && current >= 0 && target != current)
        {
            var before = CaptureLefts();

            shell.DropTab(_dragging, target);

            // Layout has to settle before the new positions can be measured.
            _strip.UpdateLayout();
            AnimateFrom(before);

            // The dragged tab now sits in a new slot. Rebase the press origin onto
            // it so its offset stays relative to its current home.
            if (LeftOf(_dragging) is { } left)
            {
                _pressOrigin = _pressOrigin.WithX(_pressOrigin.X + (left - _dragOriginX));
                _dragOriginX = left;
            }
        }

        // The dragged tab follows the pointer exactly.
        Offset(_dragging, position.X - _pressOrigin.X);

        // Preview on whichever other window's strip the pointer is over, so the
        // user can see where a cross-window drop would land.
        if (TopLevel.GetTopLevel(_strip) is MainWindow owner && Manager is { } manager)
        {
            manager.UpdateDropPreview(owner.PointToScreen(e.GetPosition(owner)), owner);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) =>
        EndDrag(e);

    /// <summary>Escape abandons the drag, leaving the strip as it stands.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isDragging)
        {
            EndDrag(release: null);
            e.Handled = true;
        }
    }

    private void EndDrag(PointerReleasedEventArgs? release)
    {
        var dragged = _dragging;
        var wasDragging = _isDragging;

        _dragging = null;
        _isDragging = false;

        Manager?.ClearDropPreview();

        if (dragged is null)
        {
            return;
        }

        if (ContainerFor(dragged) is { } container)
        {
            container.ZIndex = 0;
            container.Transitions = SlideTransitions();
            container.RenderTransform = NoOffset;
        }

        if (wasDragging && release is not null)
        {
            CompleteAcrossWindows(dragged, release);
        }
    }

    /// <summary>
    /// Handles a release that left this window's tab strip: the tab either joins
    /// another window's strip or becomes a window of its own.
    /// </summary>
    private void CompleteAcrossWindows(TabItemViewModel dragged, PointerReleasedEventArgs e)
    {
        if (_shell() is not { } shell ||
            Manager is not { } manager ||
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

            if (shell.StripItems.Count == 0)
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

    /// <summary>Records where every strip item currently sits, keyed by its view model.</summary>
    private Dictionary<object, double> CaptureLefts()
    {
        var positions = new Dictionary<object, double>();

        foreach (var container in _strip.GetRealizedContainers())
        {
            if (container.DataContext is { } key &&
                container.TranslatePoint(default, _strip) is { } origin)
            {
                positions[key] = origin.X;
            }
        }

        return positions;
    }

    /// <summary>
    /// Offsets each displaced container back to where it was, then releases it so
    /// the transition slides it to its new place.
    /// </summary>
    private void AnimateFrom(Dictionary<object, double> before)
    {
        foreach (var container in _strip.GetRealizedContainers())
        {
            if (container.DataContext is not { } key ||
                ReferenceEquals(key, _dragging) ||
                !before.TryGetValue(key, out var previous))
            {
                continue;
            }

            if (container.TranslatePoint(default, _strip) is not { } origin)
            {
                continue;
            }

            var delta = previous - origin.X;

            if (Math.Abs(delta) < 0.5)
            {
                continue;
            }

            container.Transitions = null;
            container.RenderTransform = TransformOperations.Parse(
                string.Create(CultureInfo.InvariantCulture, $"translate({delta}px, 0px)"));
            container.Transitions = SlideTransitions();
            container.RenderTransform = NoOffset;
        }
    }

    private static Transitions SlideTransitions() =>
    [
        new TransformOperationsTransition
        {
            Property = Visual.RenderTransformProperty,
            Duration = TimeSpan.FromMilliseconds(160),
            Easing = new CubicEaseOut(),
        },
    ];

    private void Offset(TabItemViewModel tab, double x)
    {
        if (ContainerFor(tab) is not { } container)
        {
            return;
        }

        container.RenderTransform = Math.Abs(x) < 0.5
            ? NoOffset
            : TransformOperations.Parse(
                string.Create(CultureInfo.InvariantCulture, $"translate({x}px, 0px)"));
    }

    private double? LeftOf(TabItemViewModel tab) =>
        ContainerFor(tab)?.TranslatePoint(default, _strip)?.X;

    private Control? ContainerFor(TabItemViewModel tab)
    {
        foreach (var container in _strip.GetRealizedContainers())
        {
            if (ReferenceEquals(container.DataContext, tab))
            {
                return container;
            }
        }

        return null;
    }

    private static TabItemViewModel? TabUnder(Visual? visual)
    {
        for (var element = visual as StyledElement; element is not null; element = element.Parent)
        {
            if (element.DataContext is TabItemViewModel tab)
            {
                return tab;
            }
        }

        return null;
    }

    /// <summary>
    /// The session index the dragged tab should occupy for a pointer at
    /// <paramref name="x"/>. Group chips occupy strip space but no session index,
    /// so only tab containers are counted. A tab is displaced once the pointer
    /// passes its midpoint, so tabs swap under the cursor rather than only when it
    /// reaches their far edge.
    /// </summary>
    private int TabIndexAt(double x)
    {
        var index = 0;

        foreach (var container in _strip.GetRealizedContainers())
        {
            if (container.DataContext is not TabItemViewModel)
            {
                continue;
            }

            if (container.TranslatePoint(default, _strip) is { } origin &&
                x < origin.X + container.Bounds.Width / 2)
            {
                return index;
            }

            index++;
        }

        return Math.Max(0, index - 1);
    }
}
