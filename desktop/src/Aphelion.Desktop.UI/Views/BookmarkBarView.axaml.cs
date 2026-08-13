using System.ComponentModel;
using Aphelion.Desktop.UI.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

namespace Aphelion.Desktop.UI.Views;

public partial class BookmarkBarView : UserControl
{
    /// <summary>
    /// How long the pointer must rest on a row before its folder opens, in
    /// milliseconds. Short enough to feel immediate once browsing has started,
    /// long enough that crossing the bar does not open everything on the way.
    /// </summary>
    private const int HoverOpenDelay = 220;

    private readonly BookmarkBarDragHandler _drag;

    private DispatcherTimer? _hoverTimer;
    private BookmarkNodeViewModel? _hoverCandidate;

    /// <summary>The list inside each open folder dropdown, and the folder it shows.</summary>
    private readonly Dictionary<ItemsControl, BookmarkNodeViewModel> _folderLists = [];

    private BookmarksViewModel? _watched;

    public BookmarkBarView()
    {
        InitializeComponent();

        // Rooted on the items host rather than on this control: the host is what
        // the rows live in, and it survives the collection being reconciled
        // underneath a drag in progress.
        _drag = new BookmarkBarDragHandler(
            BarItemsHost,
            () => DataContext as BookmarksViewModel);

        // One handler covers every level. A drag begun on the bar has to be able
        // to reach rows inside open dropdowns, and those live in separate
        // top-level windows the handler cannot find on its own.
        _drag.OpenFolderLists = () => _folderLists
            .Select(entry => ((Control)entry.Key, entry.Value));

        _drag.DropFeedbackChanged += (_, _) => ApplyDropFeedback();
    }

    /// <summary>
    /// Pushes the drag's current intent onto the rows, which draw it: a marker in
    /// front of the row a drop would land at, or a highlight on the folder it
    /// would be filed into.
    /// </summary>
    private void ApplyDropFeedback()
    {
        if (DataContext is not BookmarksViewModel bookmarks)
        {
            return;
        }

        // Every level, not just the bar: the drop being described may be inside
        // an open dropdown, and the rows left behind on the bar have to stop
        // showing feedback that no longer applies.
        foreach (var row in bookmarks.BarItems)
        {
            Mark(row);
        }

        void Mark(BookmarkNodeViewModel row)
        {
            row.IsDropBefore = ReferenceEquals(row, _drag.DropTarget);
            row.IsDropTarget = ReferenceEquals(row, _drag.DropIntoFolder);

            if (row.Children is null)
            {
                return;
            }

            foreach (var child in row.Children)
            {
                Mark(child);
            }
        }

        // A drop past the last row belongs to no row, so the bar draws that one
        // marker itself, parked at the end of the strip.
        TrailingDropMarker.IsVisible =
            _drag is { DropTarget: null, DropIntoFolder: null } && _drag.IsDragging;

        if (TrailingDropMarker.IsVisible)
        {
            TrailingDropMarker.Margin = new Avalonia.Thickness(TrailingMarkerOffset(), 4, 0, 4);
        }
    }

    /// <summary>Where the end-of-strip marker sits: just past the last row.</summary>
    private double TrailingMarkerOffset()
    {
        var right = 0d;

        foreach (var container in BarItemsHost.GetRealizedContainers())
        {
            if (container.TranslatePoint(default, this) is { } origin)
            {
                right = Math.Max(right, origin.X + container.Bounds.Width);
            }
        }

        return right + 1;
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

        if (_hoverCandidate is not { } node)
        {
            return;
        }

        var bookmarks = node.Owner;

        // Nothing is open yet, so the pointer merely passing over the bar should
        // not start opening things; a click is what begins browsing.
        if (!bookmarks.BarItems.Any(row => row.IsOpen))
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
        if (sender is Popup { DataContext: BookmarkNodeViewModel node })
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

    /// <summary>
    /// Abandons a rename dismissed by clicking away, so the view model's state
    /// matches what is on screen.
    /// </summary>
    private void OnRenamePopupClosed(object? sender, EventArgs e)
    {
        if (DataContext is BookmarksViewModel { IsRenaming: true } bookmarks)
        {
            Dispatcher.UIThread.Post(
                () => bookmarks.CancelRenameCommand.Execute(null),
                DispatcherPriority.Background);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnBookmarksPropertyChanged;
        }

        _watched = DataContext as BookmarksViewModel;

        if (_watched is not null)
        {
            _watched.PropertyChanged += OnBookmarksPropertyChanged;
        }

        base.OnDataContextChanged(e);
    }

    private void OnBookmarksPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The rename box is only useful with the caret already in it and the old
        // name selected, ready to be typed over.
        if (e.PropertyName == nameof(BookmarksViewModel.IsRenaming) &&
            _watched is { IsRenaming: true })
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
