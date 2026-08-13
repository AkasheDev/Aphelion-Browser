using System.Collections.ObjectModel;
using Aphelion.Desktop.UI.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aphelion.Desktop.UI.Views;

/// <summary>
/// Dragging a row along the bookmark bar: reorder it, or drop it onto a folder
/// to file it inside.
/// </summary>
/// <remarks>
/// Shares its shape with <see cref="TabListDragHandler"/> — tunnel handlers on a
/// stable root, a travel threshold before a press counts as a drag, pointer
/// capture so the gesture survives leaving the panel, Escape to abandon — with
/// the differences the bar forces.
///
/// The bar runs across, so placement is decided on X. A bookmark row is a
/// Button, where a tab row is a Border, and that matters twice: the tab handler
/// treats a Button as a control inside a row and bails out, whereas here the
/// Button is the row; and a Button captures the pointer on press, which would
/// starve this handler of the moves that make a drag. The capture is taken back
/// on the first move, and the row's own click is suppressed for the press that
/// turned into a drag.
///
/// Feedback while dragging is drawn by the view, from <see cref="DropTarget"/>
/// and <see cref="DropIntoFolder"/>.
/// </remarks>
internal sealed class BookmarkBarDragHandler
{
    /// <summary>Pointer travel before a press becomes a drag, in pixels.</summary>
    private const double DragThreshold = 5;

    /// <summary>
    /// How long a drag must rest on a folder before that folder springs open, in
    /// milliseconds. Long enough that sweeping across the bar does not open
    /// everything it passes, short enough not to feel stuck.
    /// </summary>
    private const int SpringOpenDelay = 400;

    private readonly Control _root;
    private readonly Func<BookmarksViewModel?> _bookmarks;

    /// <summary>
    /// The folder whose contents this handler is dragging within, or null for the
    /// bar itself. Drops that are not filed into a folder land here.
    /// </summary>
    private readonly Func<BookmarkNodeViewModel?> _container;

    /// <summary>
    /// Whether rows are stacked down rather than across, which is the case
    /// inside a folder dropdown and decides the axis every drop test uses.
    /// </summary>
    private readonly bool _isVertical;

    private DispatcherTimer? _springTimer;
    private BookmarkNodeViewModel? _springCandidate;

    private BookmarkNodeViewModel? _pressed;
    private Button? _row;
    private Point _pressOrigin;
    private bool _isDragging;
    private IPointer? _capturedPointer;

    public BookmarkBarDragHandler(
        Control root,
        Func<BookmarksViewModel?> bookmarks,
        Func<BookmarkNodeViewModel?>? container = null,
        bool isVertical = false)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _bookmarks = bookmarks ?? throw new ArgumentNullException(nameof(bookmarks));
        _container = container ?? (static () => null);
        _isVertical = isVertical;

        _root.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _root.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _root.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _root.AddHandler(InputElement.KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Whether a drag is currently running.</summary>
    public bool IsDragging => _isDragging;

    /// <summary>The row a drop would land in front of, while a drag is running.</summary>
    public BookmarkNodeViewModel? DropTarget { get; private set; }

    /// <summary>The folder a drop would file into, while a drag is running.</summary>
    public BookmarkNodeViewModel? DropIntoFolder { get; private set; }

    /// <summary>
    /// The folder whose contents a reordering drop would land among, or null for
    /// the bar. Follows the pointer into open dropdowns rather than staying with
    /// the list the drag began in.
    /// </summary>
    public BookmarkNodeViewModel? DropParent { get; private set; }

    /// <summary>Raised whenever the drop feedback changes, so the view can redraw.</summary>
    public event EventHandler? DropFeedbackChanged;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Reset();

        if (!e.GetCurrentPoint(_root).Properties.IsLeftButtonPressed ||
            e.Source is not Visual source ||
            RowFrom(source) is not { } row ||
            row.DataContext is not BookmarkNodeViewModel node)
        {
            return;
        }

        _pressed = node;
        _row = row;
        _pressOrigin = e.GetPosition(_root);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is null)
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

            // The row's Button took the pointer on press. Taking it onto the
            // stable root keeps the moves coming after the pointer leaves that
            // row — which it does immediately — and lets the drag survive the
            // rows being reconciled underneath it.
            e.Pointer.Capture(_root);
            _capturedPointer = e.Pointer;

            if (_row is not null)
            {
                _row.Opacity = 0.4;
            }

            // Folders are deliberately left open. Closing them would take away
            // the very rows a drag needs to reach: filing something into a
            // folder inside a folder means dropping it on a row that only exists
            // while its dropdown is showing.
        }

        UpdateDropFeedback(position, e);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var dragged = _pressed;
        var wasDragging = _isDragging;
        var into = DropIntoFolder;
        var before = DropTarget;
        var parentRow = DropParent;

        Reset();

        if (dragged is null || !wasDragging || _bookmarks() is not { } bookmarks)
        {
            // A press that never travelled is a click, and belongs to the row's
            // own command rather than to this handler.
            return;
        }

        // The press became a drag, so the row's click must not also fire.
        e.Handled = true;

        if (into is not null && bookmarks.Tree.FindFolder(into.Id) is { } destination)
        {
            bookmarks.Move(dragged.Id, destination);
            return;
        }

        // Not filed into anything, so it lands among the rows the pointer was
        // last over — which may be a dropdown the drag wandered into, not the
        // list it started from.
        var parent = parentRow is { } owner
            ? bookmarks.Tree.FindFolder(owner.Id) ?? bookmarks.Tree.Root
            : bookmarks.Tree.Root;

        bookmarks.Move(dragged.Id, parent, before?.Id);
    }

    /// <summary>Escape abandons the drag, leaving the row where it was.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isDragging)
        {
            Reset();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Works out what the current pointer position means: filing into a folder,
    /// or landing between two rows.
    /// </summary>
    /// <remarks>
    /// The middle of a folder row files into it and its outer thirds reorder
    /// around it, which is how Chrome's own bookmark manager separates the two
    /// gestures. A bookmark row has no inside to drop into, so all of it
    /// reorders.
    /// </remarks>
    private void UpdateDropFeedback(Point position, PointerEventArgs e)
    {
        var previousTarget = DropTarget;
        var previousFolder = DropIntoFolder;

        DropTarget = null;
        DropIntoFolder = null;
        DropParent = null;

        // An open dropdown is a separate top-level window, so a row inside one is
        // nowhere in this handler's own tree. Screen coordinates are the common
        // frame the two can be compared in, and testing dropdowns first means the
        // one lying over the bar wins the rows underneath it.
        if (HitTestOpenFolders(e) is { } inPopup)
        {
            Resolve(inPopup.Row, inPopup.Node, inPopup.Position, vertical: true);
            DropParent = inPopup.Owner;
            RaiseIfChanged(previousTarget, previousFolder);
            return;
        }

        foreach (var row in Rows())
        {
            if (row.DataContext is not BookmarkNodeViewModel node ||
                ReferenceEquals(node, _pressed) ||
                row.TranslatePoint(default, _root) is not { } origin)
            {
                continue;
            }

            var start = Along(origin);
            var extent = _isVertical ? row.Bounds.Height : row.Bounds.Width;
            var at = Along(position);

            if (at > start + extent)
            {
                continue;
            }

            Resolve(row, node, position, _isVertical);
            DropParent = _container();
            break;
        }

        RaiseIfChanged(previousTarget, previousFolder);
    }

    /// <summary>
    /// Decides what a position over one row means: filing into it, or landing
    /// beside it.
    /// </summary>
    /// <remarks>
    /// The middle of a folder row files into it and its outer thirds reorder
    /// around it, which is how Chrome's own bookmark manager separates the two
    /// gestures. A bookmark row has no inside to drop into, so all of it
    /// reorders.
    /// </remarks>
    private void Resolve(Control row, BookmarkNodeViewModel node, Point position, bool vertical)
    {
        // A row in a dropdown is measured in its own coordinates — the position
        // handed in is already relative to it — while a row in this handler's own
        // tree is measured against the root the drag is tracked in.
        var origin = vertical == _isVertical
            ? row.TranslatePoint(default, _root) ?? default
            : default;

        var start = vertical ? origin.Y : origin.X;
        var extent = vertical ? row.Bounds.Height : row.Bounds.Width;
        var at = vertical ? position.Y : position.X;

        if (node.IsFolder && at >= start + extent / 3 && at <= start + extent * 2 / 3)
        {
            DropIntoFolder = node;
        }
        else if (at < start + extent / 2)
        {
            DropTarget = node;
        }
        else
        {
            // Past this row's midpoint: land in front of whatever follows it.
            DropTarget = RowAfterIn(node, SiblingsOf(node));
        }
    }

    private void RaiseIfChanged(
        BookmarkNodeViewModel? previousTarget,
        BookmarkNodeViewModel? previousFolder)
    {
        if (!ReferenceEquals(previousTarget, DropTarget) ||
            !ReferenceEquals(previousFolder, DropIntoFolder))
        {
            DropFeedbackChanged?.Invoke(this, EventArgs.Empty);
        }

        UpdateSpringOpen();
    }

    /// <summary>
    /// Finds the row under the pointer in any open folder dropdown, preferring
    /// the innermost so a nested menu wins over the one it overlaps.
    /// </summary>
    /// <remarks>
    /// The dropdowns are registered by the view as they open, since only it can
    /// see the lists inside them; each is a separate top-level window, so the
    /// pointer is asked for its position relative to each list in turn rather
    /// than compared in this handler's own coordinates.
    /// </remarks>
    private FolderHit? HitTestOpenFolders(PointerEventArgs e)
    {
        FolderHit? best = null;
        var depth = -1;

        foreach (var (list, owner) in OpenFolderLists())
        {
            if (owner.Children is null || !owner.IsOpen)
            {
                continue;
            }

            var level = Depth(owner);

            if (level <= depth)
            {
                continue;
            }

            foreach (var row in list.GetVisualDescendants()
                         .OfType<Button>()
                         .Where(b => b.Classes.Contains("bookmark-item")))
            {
                if (row.DataContext is not BookmarkNodeViewModel node ||
                    ReferenceEquals(node, _pressed))
                {
                    continue;
                }

                var local = e.GetPosition(row);

                if (local.X < 0 || local.Y < 0 ||
                    local.X > row.Bounds.Width || local.Y > row.Bounds.Height)
                {
                    continue;
                }

                depth = level;
                best = new FolderHit(row, node, local, owner);
                break;
            }
        }

        return best;
    }

    /// <summary>How many folders deep a row sits, for preferring inner menus.</summary>
    private int Depth(BookmarkNodeViewModel folder)
    {
        if (_bookmarks() is not { } bookmarks)
        {
            return 0;
        }

        return bookmarks.BarItems.Contains(folder) ? 0 : 1 + DepthOfParent(bookmarks, folder);
    }

    private static int DepthOfParent(BookmarksViewModel bookmarks, BookmarkNodeViewModel folder)
    {
        var level = 0;

        foreach (var root in bookmarks.BarItems)
        {
            if (Search(root, folder, 0) is { } found)
            {
                level = found;
                break;
            }
        }

        return level;

        static int? Search(BookmarkNodeViewModel row, BookmarkNodeViewModel target, int level)
        {
            if (row.Children is not { } children)
            {
                return null;
            }

            foreach (var child in children)
            {
                if (ReferenceEquals(child, target))
                {
                    return level;
                }

                if (Search(child, target, level + 1) is { } found)
                {
                    return found;
                }
            }

            return null;
        }
    }

    /// <summary>The dropdown lists currently on screen, supplied by the view.</summary>
    public Func<IEnumerable<(Control List, BookmarkNodeViewModel Owner)>> OpenFolderLists { get; set; } =
        static () => [];

    private sealed record FolderHit(
        Button Row,
        BookmarkNodeViewModel Node,
        Point Position,
        BookmarkNodeViewModel Owner);

    /// <summary>
    /// Opens a folder the drag has been resting on, so its contents can be
    /// dropped into directly. Chrome does the same; without it a bookmark can
    /// only ever be filed one level at a time.
    /// </summary>
    private void UpdateSpringOpen()
    {
        if (ReferenceEquals(_springCandidate, DropIntoFolder))
        {
            return;
        }

        _springCandidate = DropIntoFolder;
        _springTimer?.Stop();

        if (_springCandidate is null)
        {
            return;
        }

        _springTimer ??= new DispatcherTimer();
        _springTimer.Interval = TimeSpan.FromMilliseconds(SpringOpenDelay);
        _springTimer.Tick -= OnSpringTick;
        _springTimer.Tick += OnSpringTick;
        _springTimer.Start();
    }

    private void OnSpringTick(object? sender, EventArgs e)
    {
        _springTimer?.Stop();

        // Still resting on the same folder when the delay elapsed.
        if (_isDragging && _springCandidate is { IsFolder: true } folder)
        {
            folder.IsOpen = true;
        }
    }

    /// <summary>The coordinate that matters for this handler's axis.</summary>
    private double Along(Point point) => _isVertical ? point.Y : point.X;

    /// <summary>The rows a given row is listed among.</summary>
    private ObservableCollection<BookmarkNodeViewModel> SiblingsOf(BookmarkNodeViewModel node)
    {
        if (_bookmarks() is not { } bookmarks)
        {
            return [];
        }

        if (bookmarks.BarItems.Contains(node))
        {
            return bookmarks.BarItems;
        }

        foreach (var root in bookmarks.BarItems)
        {
            if (Find(root, node) is { } owner)
            {
                return owner;
            }
        }

        return bookmarks.BarItems;

        static ObservableCollection<BookmarkNodeViewModel>? Find(
            BookmarkNodeViewModel row,
            BookmarkNodeViewModel target)
        {
            if (row.Children is not { } children)
            {
                return null;
            }

            if (children.Contains(target))
            {
                return children;
            }

            foreach (var child in children)
            {
                if (Find(child, target) is { } found)
                {
                    return found;
                }
            }

            return null;
        }
    }

    private BookmarkNodeViewModel? RowAfterIn(
        BookmarkNodeViewModel node,
        ObservableCollection<BookmarkNodeViewModel> siblings)
    {
        var index = -1;

        for (var i = 0; i < siblings.Count; i++)
        {
            if (ReferenceEquals(siblings[i], node))
            {
                index = i;
                break;
            }
        }

        for (var next = index + 1; index >= 0 && next < siblings.Count; next++)
        {
            if (!ReferenceEquals(siblings[next], _pressed))
            {
                return siblings[next];
            }
        }

        return null;
    }

    private IEnumerable<Button> Rows() =>
        _root.GetVisualDescendants()
            .OfType<Button>()
            .Where(row => row.Classes.Contains("bookmark-item"))
            .OrderBy(row => row.TranslatePoint(default, _root) is { } o ? Along(o) : double.MaxValue);

    private void Reset()
    {
        if (_row is not null)
        {
            _row.ClearValue(Visual.OpacityProperty);
        }

        _springTimer?.Stop();
        _springCandidate = null;

        _capturedPointer?.Capture(null);
        _capturedPointer = null;

        _pressed = null;
        _row = null;
        _isDragging = false;

        if (DropTarget is not null || DropIntoFolder is not null)
        {
            DropTarget = null;
            DropIntoFolder = null;
            DropParent = null;
            DropFeedbackChanged?.Invoke(this, EventArgs.Empty);
        }

        DropParent = null;
    }

    private static Button? RowFrom(Visual? source)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Button row && row.Classes.Contains("bookmark-item"))
            {
                return row;
            }
        }

        return null;
    }
}
