using System.Globalization;
using Aphelion.Desktop.UI.ViewModels;
using Aphelion.Desktop.Domain.ValueObjects;
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

    /// <summary>
    /// How far the dragged tab may peel vertically with the pointer. The strip
    /// clips its children, so following the full distance would just slide the tab
    /// under the edge; a bounded peel plus fading reads as "about to detach".
    /// </summary>
    private const double PeelLimit = 12;

    private static readonly TransformOperations NoOffset = TransformOperations.Parse("translate(0px, 0px)");

    private readonly ItemsControl _strip;
    private readonly Func<ShellViewModel?> _shell;

    private TabItemViewModel? _dragging;

    /// <summary>
    /// The group chip being dragged, when the press landed on one. A group moves
    /// as a whole run rather than as a member, so it is tracked separately from
    /// the dragged tab and never both at once.
    /// </summary>
    private GroupHeaderViewModel? _draggingGroup;

    /// <summary>Where the dragged group's run was last placed, to avoid re-dropping.</summary>
    private int _lastGroupIndex = -1;

    private Point _pressOrigin;
    private bool _isDragging;
    private TabId? _lastBeforeId;
    private TabGroupId? _lastGroup;
    private bool _hasDropSignature;

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
        var point = e.GetCurrentPoint(_strip);

        // Middle-click closes the tab under the pointer, as in every browser.
        if (point.Properties.IsMiddleButtonPressed)
        {
            if (TabUnder(e.Source as Visual) is { } victim && _shell() is { } owner)
            {
                owner.CloseTabCommand.Execute(victim);
                e.Handled = true;
            }

            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        // A group chip is a Button, so its click is its own — but a press that
        // travels is a drag of the whole run. Recorded here and only acted on once
        // the pointer has moved, which leaves a plain click collapsing the group
        // as before.
        if (GroupUnder(e.Source as Visual) is { } chip)
        {
            _draggingGroup = chip;
            _pressOrigin = e.GetPosition(_strip);
            _isDragging = false;
            _lastGroupIndex = -1;
            return;
        }

        // Buttons — the close button — own their own clicks.
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        _dragging = TabUnder(e.Source as Visual);
        _pressOrigin = e.GetPosition(_strip);
        _isDragging = false;
    }

    /// <summary>
    /// Slides a whole group run along the strip as the pointer moves, the way a
    /// dragged tab slides between its neighbours.
    /// </summary>
    private void DragGroup(PointerEventArgs e)
    {
        if (_draggingGroup is not { } chip || _shell() is not { } shell)
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
        }

        // Pulled clear of the strip the drag is on its way somewhere else — the
        // bookmark bar, or a window of its own. Reordering the run under the
        // pointer on the way past would shuffle the strip for a gesture that was
        // never about the strip.
        if (position.Y < -TearOffMargin || position.Y > _strip.Bounds.Height + TearOffMargin)
        {
            return;
        }

        var index = GroupIndexAt(position.X, chip.Id);

        if (index == _lastGroupIndex)
        {
            return;
        }

        var before = CaptureLefts();

        shell.DropGroup(chip.Id, index);
        _lastGroupIndex = index;

        // Layout has to settle before the new positions can be measured, so the
        // neighbours can be animated from where they were.
        _strip.UpdateLayout();
        AnimateFrom(before);
    }

    /// <summary>
    /// Where a group dropped at <paramref name="x"/> belongs, counted in tabs that
    /// are not its own — which is the position
    /// <see cref="Domain.Entities.BrowsingSession.MoveGroup"/> works in.
    /// </summary>
    private int GroupIndexAt(double x, Domain.ValueObjects.TabGroupId groupId)
    {
        var index = 0;

        foreach (var container in _strip.GetRealizedContainers())
        {
            if (container.DataContext is not TabItemViewModel tab ||
                tab.Tab.GroupId == groupId ||
                container.TranslatePoint(default, _strip) is not { } origin)
            {
                continue;
            }

            if (x > origin.X + container.Bounds.Width / 2)
            {
                index++;
            }
        }

        return index;
    }

    private static GroupHeaderViewModel? GroupUnder(Visual? visual)
    {
        for (var element = visual as StyledElement; element is not null; element = element.Parent)
        {
            if (element.DataContext is GroupHeaderViewModel chip)
            {
                return chip;
            }
        }

        return null;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingGroup is not null)
        {
            DragGroup(e);
            return;
        }

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
            _lastBeforeId = null;
            _lastGroup = null;
            _hasDropSignature = false;
            _dragOriginX = LeftOf(_dragging) ?? position.X;

            if (ContainerFor(_dragging) is { } lifted)
            {
                // Above its neighbours, and no easing: an eased dragged tab lags
                // behind the cursor.
                lifted.ZIndex = 10;
                lifted.Transitions = null;
            }
        }

        // Beyond the strip vertically the drag is a tear-off in progress: the strip
        // stops reordering and the tab fades to say it is about to leave.
        var inTearZone = position.Y < -TearOffMargin ||
                         position.Y > _strip.Bounds.Height + TearOffMargin;

        if (!inTearZone)
        {
            var beforeTab = TabBeforeAt(position.X);
            var group = GroupHintAt(position.X);
            var beforeId = beforeTab?.Id;

            if (!_hasDropSignature || beforeId != _lastBeforeId || group != _lastGroup)
            {
                var before = CaptureLefts();

                shell.DropTab(_dragging, beforeTab, group);
                _lastBeforeId = beforeId;
                _lastGroup = group;
                _hasDropSignature = true;

                // Layout has to settle before the new positions can be measured.
                _strip.UpdateLayout();
                AnimateFrom(before);

                // The dragged tab now sits in a new slot. Rebase the press origin
                // onto it so its offset stays relative to its current home.
                if (LeftOf(_dragging) is { } left)
                {
                    _pressOrigin = _pressOrigin.WithX(_pressOrigin.X + (left - _dragOriginX));
                    _dragOriginX = left;
                }
            }
        }

        // The dragged tab follows the pointer exactly in X, and peels a bounded
        // distance in Y so pulling away from the strip is visible.
        Offset(
            _dragging,
            position.X - _pressOrigin.X,
            Math.Clamp(position.Y - _pressOrigin.Y, -PeelLimit, PeelLimit));

        if (ContainerFor(_dragging) is { } hinted)
        {
            hinted.Opacity = inTearZone ? 0.65 : 1.0;
        }

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
        var draggedGroup = _draggingGroup;
        var wasDragging = _isDragging;

        _dragging = null;
        _draggingGroup = null;
        _isDragging = false;
        _lastGroupIndex = -1;

        Manager?.ClearDropPreview();

        if (draggedGroup is not null)
        {
            // Reordering inside the strip has already happened live as the pointer
            // moved. What is left is a release that landed somewhere else, and
            // stopping the chip's own click from collapsing the group as well.
            if (wasDragging && release is not null)
            {
                release.Handled = true;
                CompleteGroupDrop(draggedGroup, release);
            }

            return;
        }

        if (dragged is null)
        {
            return;
        }

        if (ContainerFor(dragged) is { } container)
        {
            container.ZIndex = 0;
            container.Opacity = 1.0;
            container.Transitions = SlideTransitions();
            container.RenderTransform = NoOffset;
        }

        if (wasDragging && release is not null)
        {
            CompleteAcrossWindows(dragged, release);
        }
    }

    /// <summary>
    /// Handles a group released away from the strip: onto the bookmark bar, which
    /// files its chip there, or clear of everything, which gives it a window.
    /// </summary>
    private void CompleteGroupDrop(GroupHeaderViewModel chip, PointerReleasedEventArgs e)
    {
        if (_shell() is not { } shell ||
            shell.Bookmarks is not { } bookmarks ||
            TopLevel.GetTopLevel(_strip) is not MainWindow owner)
        {
            return;
        }

        var local = e.GetPosition(_strip);

        // Still over the strip: the live reorder already placed it.
        if (local.Y >= -TearOffMargin && local.Y <= _strip.Bounds.Height + TearOffMargin)
        {
            return;
        }

        if (bookmarks.SavedGroupRowIdFor(shell, chip.Id.ToString()) is not { } rowId)
        {
            return;
        }

        // The bar is tested before the tear-off, since it lies below the strip and
        // so is always past the tear margin. Dropping onto it means "put the chip
        // here", not "give this group a window".
        var screen = owner.PointToScreen(e.GetPosition(owner));

        if (owner.GetVisualDescendants().OfType<BookmarkBarView>().FirstOrDefault() is { } bar &&
            bar.TryDropSavedGroup(screen, rowId))
        {
            return;
        }

        // Clear of the window's chrome entirely: the group leaves for one of its
        // own, taking its tabs with it. Reopening it in a new window is already how
        // a saved group is transferred, so the tear-off borrows that path whole.
        if (bookmarks.Tree.FindFolder(rowId) is { } folder)
        {
            shell.OpenSavedGroup(folder, BookmarkOpenTarget.NewWindow);
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
        TabTransfer.Complete(shell, manager, owner, dragged, screenPoint, allowSameWindow: false);
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
            Duration = TimeSpan.FromMilliseconds(210),
            Easing = new CubicEaseOut(),
        },
    ];

    private void Offset(TabItemViewModel tab, double x, double y = 0)
    {
        if (ContainerFor(tab) is not { } container)
        {
            return;
        }

        container.RenderTransform = _isDragging
            ? TransformOperations.Parse(
                string.Create(CultureInfo.InvariantCulture, $"translate({x}px, {y}px) scale(1.025)"))
            : NoOffset;
    }

    /// <summary>
    /// The group a tab dropped at <paramref name="x"/> would join: the group of the
    /// tab or chip directly under the pointer. Hovering anywhere over a grouped
    /// tab, or over the chip itself, joins that group; hovering over a loose tab or
    /// empty space leaves it. A collapsed group refuses drops, since the tab would
    /// vanish into it.
    /// </summary>
    private Domain.ValueObjects.TabGroupId? GroupHintAt(double x)
    {
        foreach (var container in _strip.GetRealizedContainers())
        {
            if (container.TranslatePoint(default, _strip) is not { } origin ||
                x < origin.X ||
                x >= origin.X + container.Bounds.Width)
            {
                continue;
            }

            return container.DataContext switch
            {
                TabItemViewModel tab => tab.Tab.GroupId,
                GroupHeaderViewModel { IsCollapsed: false } chip => chip.Id,
                _ => null,
            };
        }

        return null;
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
    /// The visible tab that should follow the dragged entry at <paramref name="x"/>.
    /// Identity is used instead of an integer because the rendered strip omits
    /// overflow tabs, collapsed members and hidden split partners.
    /// </summary>
    private TabItemViewModel? TabBeforeAt(double x)
    {
        foreach (var container in _strip.GetRealizedContainers())
        {
            if (container.DataContext is not TabItemViewModel tab || ReferenceEquals(tab, _dragging))
            {
                continue;
            }

            if (container.TranslatePoint(default, _strip) is { } origin &&
                x < origin.X + container.Bounds.Width / 2)
            {
                return tab;
            }
        }

        return null;
    }
}
