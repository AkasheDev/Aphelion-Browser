using System.ComponentModel;
using Aphelion.Desktop.Domain.ValueObjects;
using Aphelion.Desktop.UI.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aphelion.Desktop.UI.Views;

public partial class BookmarkBarView : UserControl
{
    public static readonly StyledProperty<ShellViewModel?> ShellProperty =
        AvaloniaProperty.Register<BookmarkBarView, ShellViewModel?>(nameof(Shell));

    public static readonly StyledProperty<BookmarksViewModel?> BookmarksProperty =
        AvaloniaProperty.Register<BookmarkBarView, BookmarksViewModel?>(nameof(Bookmarks));

    /// <summary>The window that owns this particular rendering of the shared bar.</summary>
    public ShellViewModel? Shell
    {
        get => GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    /// <summary>The shared bookmark data rendered by this window-local view.</summary>
    public BookmarksViewModel? Bookmarks
    {
        get => GetValue(BookmarksProperty);
        set => SetValue(BookmarksProperty, value);
    }

    /// <summary>
    /// How long the pointer must rest on a row before its folder opens, in
    /// milliseconds. Short enough to feel immediate once browsing has started,
    /// long enough that crossing the bar does not open everything on the way.
    /// </summary>
    private const int HoverOpenDelay = 220;

    /// <summary>
    /// Saved groups stay immediately beside the group action while they fit, but
    /// cannot consume the whole bar when there are many of them. The cap keeps
    /// ordinary bookmarks reachable on wide and narrow windows alike.
    /// </summary>
    private const double GroupStripWidthRatio = 0.45;
    private const double GroupStripMaximumWidth = 520;

    /// <summary>
    /// How many saved groups the bar shows before the rest move under the
    /// chevron. A count rather than a width: the bar should look the same however
    /// long the group names happen to be.
    /// </summary>
    private const int MaxVisibleGroups = 4;

    /// <summary>Matches the Spacing on the group strip's StackPanel.</summary>
    private const double GroupChipSpacing = 1;

    /// <summary>How far one wheel notch travels through an overflowing group strip.</summary>
    private const double GroupStripWheelStep = 44;

    private readonly BookmarkBarDragHandler _drag;

    private DispatcherTimer? _hoverTimer;
    private BookmarkNodeViewModel? _hoverCandidate;

    /// <summary>The list inside each open folder dropdown, and the folder it shows.</summary>
    private readonly Dictionary<ItemsControl, BookmarkNodeViewModel> _folderLists = [];

    /// <summary>
    /// Folder popup controls materialised by this bar and the row each belongs
    /// to. The requested state lives on the shared row, while this local map
    /// gates the actual native popup to this bar's owning window.
    /// </summary>
    private readonly Dictionary<Popup, BookmarkNodeViewModel> _folderPopups = [];

    /// <summary>
    /// Popups that actually opened for this bar. Ownership is captured at open
    /// time because activating another window can change the active shell before
    /// the native popup delivers its Closed event.
    /// </summary>
    private readonly HashSet<Popup> _ownedPopups = [];

    private BookmarksViewModel? _watched;

    public BookmarkBarView()
    {
        InitializeComponent();

        // Rooted on the items host rather than on this control: the host is what
        // the rows live in, and it survives the collection being reconciled
        // underneath a drag in progress.
        _drag = new BookmarkBarDragHandler(
            RowsHost,
            () => Bookmarks);

        // One handler covers every level. A drag begun on the bar has to be able
        // to reach rows inside open dropdowns, and those live in separate
        // top-level windows the handler cannot find on its own.
        _drag.OpenFolderLists = () => _folderLists
            .Select(entry => ((Control)entry.Key, entry.Value));

        _drag.DropFeedbackChanged += (_, _) => ApplyDropFeedback();

        // Remember the gesture's originating window before a native context
        // menu temporarily deactivates it. Commands and transient popups then
        // stay with the bar the user actually touched.
        AddHandler(
            PointerPressedEvent,
            OnBarPointerPressed,
            Avalonia.Interactivity.RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void OnBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Shell is { } shell && Bookmarks is { } bookmarks)
        {
            bookmarks.MarkActive(shell);
        }
    }

    private bool IsPresentationOwner =>
        Shell is { } shell &&
        Bookmarks is { } bookmarks &&
        bookmarks.IsActionShell(shell);

    /// <summary>
    /// Files a saved group's row where a drag from the tab strip was released,
    /// and reports whether this bar was the thing under the pointer at all.
    /// </summary>
    /// <remarks>
    /// The strip cannot measure the bar, and the bar cannot see the strip's drag,
    /// so the drop is handed over in screen coordinates — the one frame the two
    /// share. Placing the chip is all that is left to do: a live group is already
    /// mirrored onto the bar as it changes, so the gesture decides where its chip
    /// sits rather than whether it exists.
    /// </remarks>
    public bool TryDropSavedGroup(PixelPoint screen, BookmarkNodeId rowId)
    {
        if (Bookmarks is not { } bookmarks)
        {
            return false;
        }

        var local = this.PointToClient(screen);

        if (local.X < 0 || local.Y < 0 ||
            local.X > Bounds.Width || local.Y > Bounds.Height)
        {
            return false;
        }

        BookmarkNodeId? before = null;

        foreach (var chip in GroupChipsInOrder())
        {
            if (chip.DataContext is not BookmarkNodeViewModel node ||
                chip.TranslatePoint(default, this) is not { } origin)
            {
                continue;
            }

            if (node.Id != rowId && local.X < origin.X + chip.Bounds.Width / 2)
            {
                before = node.Id;
                break;
            }
        }

        bookmarks.Move(rowId, bookmarks.Tree.Root, before);
        return true;
    }

    private IEnumerable<Button> GroupChipsInOrder() =>
        GroupItemsHost.GetVisualDescendants()
            .OfType<Button>()
            .Where(chip => chip.Classes.Contains("bookmark-group-chip"))
            .OrderBy(chip => chip.TranslatePoint(default, GroupItemsHost)?.X ?? double.MaxValue);

    private void OnRowsHostSizeChanged(object? sender, SizeChangedEventArgs e) =>
        UpdateGroupStripWidth(e.NewSize.Width);

    private void UpdateGroupStripWidth(double availableWidth)
    {
        var hasOrdinaryBookmarks = Bookmarks?.HasBarItems == true;
        var ratio = hasOrdinaryBookmarks ? GroupStripWidthRatio : 0.9;

        var cap = Math.Min(
            hasOrdinaryBookmarks ? GroupStripMaximumWidth : availableWidth,
            Math.Max(0, availableWidth * ratio));

        // The room the bar has is still the outer limit — a narrow window shows
        // fewer than the maximum rather than overrunning the bookmarks.
        if (WidthOfFirstGroups(MaxVisibleGroups) is { } counted)
        {
            cap = Math.Min(cap, counted);
        }

        // Assigned only on a real change: the cap alters the viewport, which
        // raises ScrollChanged, which recomputes the cap. Writing unconditionally
        // would make that a loop rather than a settle.
        if (Math.Abs(GroupItemsScroller.MaxWidth - cap) > 0.01)
        {
            GroupItemsScroller.MaxWidth = cap;
        }

        UpdateGroupStripFade();
    }

    /// <summary>
    /// The width the leading <paramref name="count"/> chips occupy, or null when
    /// there are no more than that many and so nothing to cut.
    /// </summary>
    private double? WidthOfFirstGroups(int count)
    {
        var chips = GroupChipsInOrder()
            .Where(chip => chip.Bounds.Width > 0)
            .ToList();

        if (chips.Count <= count)
        {
            return null;
        }

        var width = GroupChipSpacing * (count - 1);

        for (var i = 0; i < count; i++)
        {
            width += chips[i].Bounds.Width;
        }

        return width;
    }

    /// <summary>
    /// Shows the trailing fade only while the strip has groups still out of
    /// sight, and hides it again once the last one is scrolled into view.
    /// </summary>
    private void UpdateGroupStripFade()
    {
        var scroller = GroupItemsScroller;
        var hidden = scroller.Extent.Width - scroller.Viewport.Width;

        // The fade is the only sign that groups continue past the cap, so it
        // follows the scroll position rather than the strip as a whole: it goes
        // once the last group has been scrolled into view.
        GroupStripFade.IsVisible = hidden - scroller.Offset.X > 0.5;
    }

    /// <summary>
    /// Also where a group being added or removed is noticed: that changes the
    /// strip's extent, and the cap is measured from the chips themselves.
    /// </summary>
    private void OnGroupItemsScrollChanged(object? sender, ScrollChangedEventArgs e) =>
        UpdateGroupStripWidth(RowsHost.Bounds.Width);

    /// <summary>
    /// The group strip deliberately has no visible scrollbar in browser chrome;
    /// wheel and trackpad input still move it when its chips exceed the cap.
    /// </summary>
    private void OnGroupItemsPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer scroller)
        {
            return;
        }

        var limit = Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width);

        if (limit <= 0)
        {
            return;
        }

        var wheel = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)
            ? e.Delta.X
            : e.Delta.Y;
        var next = Math.Clamp(
            scroller.Offset.X - wheel * GroupStripWheelStep,
            0,
            limit);

        if (Math.Abs(next - scroller.Offset.X) < 0.01)
        {
            return;
        }

        scroller.Offset = new Vector(next, scroller.Offset.Y);
        e.Handled = true;
    }

    /// <summary>
    /// Pushes the drag's current intent onto the rows, which draw it: a marker in
    /// front of the row a drop would land at, or a highlight on the folder it
    /// would be filed into.
    /// </summary>
    private void ApplyDropFeedback()
    {
        // Drop feedback belongs to this rendering of the shared bookmark data,
        // never to its view models. Otherwise dragging in one window paints the
        // same markers in every other window.
        foreach (var root in new Control[] { RowsHost }.Concat(_folderLists.Keys))
        {
            foreach (var marker in root.GetVisualDescendants()
                         .OfType<Border>()
                         .Where(border => border.Classes.Contains("bookmark-drop-marker") &&
                             border.DataContext is BookmarkNodeViewModel))
            {
                marker.IsVisible = ReferenceEquals(marker.DataContext, _drag.DropTarget);
            }

            foreach (var button in root.GetVisualDescendants()
                         .OfType<Button>()
                         .Where(button => button.Classes.Contains("bookmark-row") &&
                             button.DataContext is BookmarkNodeViewModel))
            {
                SetClass(
                    button,
                    "drop-into",
                    ReferenceEquals(button.DataContext, _drag.DropIntoFolder));
            }
        }

        // A drop past the last row belongs to no row, so the bar draws that one
        // marker itself, parked at the end of the strip.
        TrailingDropMarker.IsVisible =
            _drag is { DropTarget: null, DropIntoFolder: null, HasDropSite: true } &&
            _drag.IsDragging;

        if (TrailingDropMarker.IsVisible)
        {
            TrailingDropMarker.Margin = new Avalonia.Thickness(TrailingMarkerOffset(), 4, 0, 4);
        }
    }

    private static void SetClass(Control control, string name, bool enabled)
    {
        if (enabled)
        {
            if (!control.Classes.Contains(name))
            {
                control.Classes.Add(name);
            }
        }
        else
        {
            control.Classes.Remove(name);
        }
    }

    /// <summary>Where the end-of-strip marker sits: just past the last row.</summary>
    private double TrailingMarkerOffset()
    {
        var right = 0d;
        var coordinateSpace = TrailingDropMarker.GetVisualParent() ?? RowsHost;

        foreach (var container in RowsHost.GetVisualDescendants()
                     .OfType<Button>()
                     .Where(button =>
                         button.Classes.Contains("bookmark-row") &&
                         button.DataContext is BookmarkNodeViewModel row &&
                         row.IsSavedGroup == _drag.IsDraggingSavedGroup &&
                         IsInsideVisibleViewport(button)))
        {
            if (VisibleRightEdge(container, coordinateSpace, RowsHost) is { } visibleRight)
            {
                right = Math.Max(right, visibleRight);
            }
        }

        return right + 1;
    }

    private static double? VisibleRightEdge(Control row, Visual coordinateSpace, Control rowsHost)
    {
        if (row.TranslatePoint(default, coordinateSpace) is not { } rowOrigin)
        {
            return null;
        }

        var left = rowOrigin.X;
        var right = rowOrigin.X + row.Bounds.Width;

        for (Visual? ancestor = row.GetVisualParent(); ancestor is not null; ancestor = ancestor.GetVisualParent())
        {
            if (ancestor is Control clip &&
                (ancestor is ScrollViewer || ReferenceEquals(ancestor, rowsHost)))
            {
                if (!clip.IsVisible || clip.TranslatePoint(default, coordinateSpace) is not { } clipOrigin)
                {
                    return null;
                }

                left = Math.Max(left, clipOrigin.X);
                right = Math.Min(right, clipOrigin.X + clip.Bounds.Width);

                if (right <= left)
                {
                    return null;
                }
            }

            if (ReferenceEquals(ancestor, rowsHost))
            {
                break;
            }
        }

        return right;
    }

    private bool IsInsideVisibleViewport(Control row)
    {
        if (!row.IsVisible || row.Bounds.Width <= 0 || row.Bounds.Height <= 0)
        {
            return false;
        }

        for (Visual? ancestor = row; ancestor is not null; ancestor = ancestor.GetVisualParent())
        {
            if (ancestor is ScrollViewer viewport &&
                (!Intersects(row, viewport) || !viewport.IsVisible))
            {
                return false;
            }

            if (ReferenceEquals(ancestor, RowsHost))
            {
                break;
            }
        }

        return Intersects(row, RowsHost);
    }

    private static bool Intersects(Control row, Control viewport)
    {
        if (row.TranslatePoint(default, viewport) is not { } origin)
        {
            return false;
        }

        return origin.X < viewport.Bounds.Width &&
            origin.Y < viewport.Bounds.Height &&
            origin.X + row.Bounds.Width > 0 &&
            origin.Y + row.Bounds.Height > 0;
    }

    /// <summary>
    /// Opens the folder the pointer comes to rest on, and follows the pointer
    /// from one folder to the next.
    /// </summary>
    /// <remarks>
    /// After a short dwell rather than the moment the pointer arrives, so that
    /// crossing the bar on the way somewhere else does not open everything it
    /// passes over. Nested folders open the same way, which is the only way to
    /// reach one without clicking through every level.
    ///
    /// A row that is not a folder still cancels a pending open, and closes the
    /// branch that was showing: moving onto a plain bookmark is a move away from
    /// whatever folder was being browsed.
    /// </remarks>
    private void OnRowPointerEntered(object? sender, PointerEventArgs e)
    {
        // The row's own Owner rather than this view's DataContext: rows inside a
        // folder dropdown live in the popup's separate visual tree, where the
        // DataContext is that row, not the bar. Reading it from the view is why
        // hovering never opened anything below the top level.
        if (sender is not Button { DataContext: BookmarkNodeViewModel node })
        {
            return;
        }

        // While dragging, opening folders is the drag handler's business — it
        // springs one open only after a longer rest, and only for a drop target.
        if (_drag.IsDragging)
        {
            _hoverTimer?.Stop();
            _hoverCandidate = null;
            return;
        }

        _hoverTimer?.Stop();
        _hoverCandidate = node;

        if (node.IsOpen)
        {
            return;
        }

        _hoverTimer ??= new DispatcherTimer();
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(HoverOpenDelay);
        _hoverTimer.Tick -= OnHoverTick;
        _hoverTimer.Tick += OnHoverTick;
        _hoverTimer.Start();
    }

    private void OnHoverTick(object? sender, EventArgs e)
    {
        _hoverTimer?.Stop();

        if (!IsPresentationOwner || _hoverCandidate is not { } node)
        {
            return;
        }

        var bookmarks = node.Owner;

        // Nothing is open yet, so the pointer merely passing over the bar should
        // not start opening things; a click is what begins browsing.
        if (!bookmarks.TopLevelRows.Any(row => row.IsOpen))
        {
            return;
        }

        bookmarks.RevealFolder(node);
    }

    /// <summary>
    /// Registers a folder's dropdown so the bar's drag handler can reach the rows
    /// inside it.
    /// </summary>
    /// <remarks>
    /// A Popup is its own top-level window, so nothing in the bar's own tree can
    /// find these rows. Rather than a handler per dropdown — which would fight
    /// the bar's for the captured pointer — the single handler is told where the
    /// open lists are and hit-tests them itself.
    /// </remarks>
    private void OnFolderItemsAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not ItemsControl host ||
            host.DataContext is not BookmarkNodeViewModel folder)
        {
            return;
        }

        _folderLists[host] = folder;
        host.DetachedFromVisualTree += (_, _) => _folderLists.Remove(host);
    }

    /// <summary>
    /// Clears the folder's open state when its popup is light-dismissed, so the
    /// row stops looking held open and can be clicked to reopen it.
    /// </summary>
    private void OnFolderPopupClosed(object? sender, EventArgs e)
    {
        if (sender is Popup { DataContext: BookmarkNodeViewModel node } popup &&
            _ownedPopups.Remove(popup))
        {
            node.IsOpen = false;

            // A dwell that was counting down towards this folder is meaningless
            // now the branch has been dismissed.
            if (ReferenceEquals(_hoverCandidate, node))
            {
                _hoverTimer?.Stop();
                _hoverCandidate = null;
            }
        }
    }

    private void OnFolderPopupOpened(object? sender, EventArgs e)
    {
        if (sender is Popup popup)
        {
            _ownedPopups.Add(popup);
        }
    }

    private void OnFolderPopupAttached(object? sender, Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        if (sender is not Popup popup)
        {
            return;
        }

        popup.DataContextChanged -= OnFolderPopupDataContextChanged;
        popup.DataContextChanged += OnFolderPopupDataContextChanged;
        HookFolderPopup(popup);
    }

    private void OnFolderPopupDetached(object? sender, Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        if (sender is not Popup popup)
        {
            return;
        }

        popup.DataContextChanged -= OnFolderPopupDataContextChanged;
        UnhookFolderPopup(popup);
    }

    private void OnFolderPopupDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is Popup popup)
        {
            UnhookFolderPopup(popup);
            HookFolderPopup(popup);
        }
    }

    private void HookFolderPopup(Popup popup)
    {
        if (popup.DataContext is not BookmarkNodeViewModel node)
        {
            return;
        }

        _folderPopups[popup] = node;
        node.PropertyChanged -= OnFolderNodePropertyChanged;
        node.PropertyChanged += OnFolderNodePropertyChanged;
        UpdateFolderPopup(popup, node);
    }

    private void UnhookFolderPopup(Popup popup)
    {
        if (!_folderPopups.TryGetValue(popup, out var node))
        {
            return;
        }

        if (_ownedPopups.Contains(popup) && node.IsOpen)
        {
            node.IsOpen = false;
        }

        _folderPopups.Remove(popup);

        if (!_folderPopups.ContainsValue(node))
        {
            node.PropertyChanged -= OnFolderNodePropertyChanged;
        }

        _ownedPopups.Remove(popup);
        popup.SetCurrentValue(Popup.IsOpenProperty, false);
    }

    private void OnFolderNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BookmarkNodeViewModel.IsOpen) ||
            sender is not BookmarkNodeViewModel node)
        {
            return;
        }

        foreach (var (popup, owner) in _folderPopups.Where(pair => ReferenceEquals(pair.Value, node)).ToList())
        {
            UpdateFolderPopup(popup, owner);
        }
    }

    private void UpdateFolderPopup(Popup popup, BookmarkNodeViewModel node) =>
        popup.SetCurrentValue(
            Popup.IsOpenProperty,
            node.IsOpen && IsPresentationOwner);

    private void UpdatePresentationPopups()
    {
        foreach (var (popup, node) in _folderPopups.ToList())
        {
            UpdateFolderPopup(popup, node);
        }

        RenamePopup.SetCurrentValue(
            Popup.IsOpenProperty,
            Bookmarks?.IsRenaming == true && IsPresentationOwner);
    }

    /// <summary>
    /// Abandons a rename dismissed by clicking away, so the view model's state
    /// matches what is on screen.
    /// </summary>
    private void OnRenamePopupClosed(object? sender, EventArgs e)
    {
        if (sender is Popup popup &&
            _ownedPopups.Remove(popup) &&
            Bookmarks is { IsRenaming: true } bookmarks)
        {
            Dispatcher.UIThread.Post(
                () => bookmarks.CancelRenameCommand.Execute(null),
                DispatcherPriority.Background);
        }
    }

    private void OnRenamePopupOpened(object? sender, EventArgs e)
    {
        if (sender is Popup popup)
        {
            _ownedPopups.Add(popup);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != BookmarksProperty)
        {
            return;
        }

        Unwatch();
        _watched = change.NewValue as BookmarksViewModel;
        BarRoot.DataContext = _watched;
        WatchIfAttached();
        UpdatePresentationPopups();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        WatchIfAttached();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Unwatch();
        _hoverTimer?.Stop();
        _hoverCandidate = null;
        _folderLists.Clear();

        foreach (var popup in _ownedPopups.ToList())
        {
            if (_folderPopups.TryGetValue(popup, out var node))
            {
                node.IsOpen = false;
            }
            else if (ReferenceEquals(popup, RenamePopup) && Bookmarks?.IsRenaming == true)
            {
                Bookmarks.CancelRenameCommand.Execute(null);
            }

            popup.SetCurrentValue(Popup.IsOpenProperty, false);
        }

        foreach (var popup in _folderPopups.Keys.ToList())
        {
            popup.DataContextChanged -= OnFolderPopupDataContextChanged;
            UnhookFolderPopup(popup);
        }

        _ownedPopups.Clear();
        RenamePopup.SetCurrentValue(Popup.IsOpenProperty, false);
        base.OnDetachedFromVisualTree(e);
    }

    private void WatchIfAttached()
    {
        var watched = _watched;

        if (watched is not null && VisualRoot is not null)
        {
            watched.PropertyChanged -= OnBookmarksPropertyChanged;
            watched.PropertyChanged += OnBookmarksPropertyChanged;
        }
    }

    private void Unwatch()
    {
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnBookmarksPropertyChanged;
        }
    }

    private void OnBookmarksPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BookmarksViewModel.HasBarItems))
        {
            UpdateGroupStripWidth(RowsHost.Bounds.Width);
        }

        if (e.PropertyName is nameof(BookmarksViewModel.ActionShell) or
            nameof(BookmarksViewModel.IsRenaming))
        {
            UpdatePresentationPopups();
        }

        // The rename box is only useful with the caret already in it and the old
        // name selected, ready to be typed over.
        if (e.PropertyName == nameof(BookmarksViewModel.IsRenaming) &&
            _watched is { IsRenaming: true } &&
            IsPresentationOwner)
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    RenameBox.Focus();
                    RenameBox.SelectAll();
                },
                DispatcherPriority.Loaded);
        }
    }
}
