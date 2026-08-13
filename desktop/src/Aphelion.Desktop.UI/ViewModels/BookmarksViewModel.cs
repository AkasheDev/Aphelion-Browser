using System.Collections.ObjectModel;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// The bookmark bar: the rows it shows, and every command those rows and the
/// folder popups beneath them invoke.
/// </summary>
/// <remarks>
/// Commands live here rather than on each row for the same reason
/// <see cref="TabListViewModel"/> centralises its own: a row in a popup cannot
/// bind to an ancestor, so it reaches its owner directly, and one owner is
/// simpler to keep correct than one command set per row.
/// </remarks>
public sealed partial class BookmarksViewModel : ViewModelBase
{
    private readonly BookmarkTree _tree;
    private readonly IBookmarkStore _store;
    private readonly IFaviconLoader? _favicons;

    public BookmarksViewModel(
        BookmarkTree tree,
        IBookmarkStore store,
        IFaviconLoader? favicons = null)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _favicons = favicons;

        SyncBarItems();
    }

    /// <summary>The top level of the tree, as shown along the bar.</summary>
    public ObservableCollection<BookmarkNodeViewModel> BarItems { get; } = [];

    public BookmarkTree Tree => _tree;

    /// <summary>Raised when a bookmark row is chosen, so the shell can navigate.</summary>
    public Action<PageAddress>? Navigate { get; set; }

    /// <summary>Raised when the set of saved addresses changes, so the star can update.</summary>
    public event EventHandler? BookmarksChanged;

    /// <summary>
    /// Opens a bookmark, or toggles a folder's dropdown. One command for both
    /// because the bar and the popups bind the same row template to it.
    /// </summary>
    [RelayCommand]
    private void Open(BookmarkNodeViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.Node is Bookmark bookmark)
        {
            CloseAllFolders();
            Navigate?.Invoke(bookmark.Address);
            return;
        }

        // Only what is beside and beneath this folder closes; the folders it sits
        // inside stay open. Closing everything shut the parent dropdown too, so a
        // folder within a folder disappeared at the moment it was opened.
        var wasOpen = item.IsOpen;
        CloseSiblingsOf(item);
        item.IsOpen = !wasOpen;
    }

    /// <summary>
    /// Shows the branch ending at <paramref name="item"/>: opens it when it is a
    /// folder, and closes whatever else was showing beside it.
    /// </summary>
    /// <remarks>
    /// This is what hovering along an open menu calls. A plain bookmark opens
    /// nothing but still closes the branch that was showing, since moving onto
    /// one is a move away from the folder being browsed.
    /// </remarks>
    public void RevealFolder(BookmarkNodeViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        CloseSiblingsOf(item);
        item.IsOpen = item.IsFolder;
    }

    /// <summary>
    /// Closes everything except <paramref name="item"/> and the folders it is
    /// nested inside — that is, its siblings at every level up to the bar, and
    /// anything already open beneath it.
    /// </summary>
    private void CloseSiblingsOf(BookmarkNodeViewModel item)
    {
        var ancestry = new HashSet<BookmarkNodeViewModel>();

        for (var walk = FindPathTo(item, BarItems); walk is not null; walk = walk.Parent)
        {
            ancestry.Add(walk.Row);
        }

        foreach (var row in BarItems)
        {
            CloseExcept(row, ancestry);
        }
    }

    private static void CloseExcept(BookmarkNodeViewModel row, HashSet<BookmarkNodeViewModel> keep)
    {
        if (!keep.Contains(row))
        {
            CloseFolders(row);
            return;
        }

        if (row.Children is null)
        {
            return;
        }

        foreach (var child in row.Children)
        {
            CloseExcept(child, keep);
        }
    }

    /// <summary>A row and the chain of folders it sits inside, nearest first.</summary>
    private sealed record RowPath(BookmarkNodeViewModel Row, RowPath? Parent);

    private static RowPath? FindPathTo(
        BookmarkNodeViewModel target,
        IEnumerable<BookmarkNodeViewModel> rows,
        RowPath? parent = null)
    {
        foreach (var row in rows)
        {
            var path = new RowPath(row, parent);

            if (ReferenceEquals(row, target))
            {
                return path;
            }

            if (row.Children is { } children && FindPathTo(target, children, path) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    [RelayCommand]
    private void Delete(BookmarkNodeViewModel? item)
    {
        if (item is null || !_tree.RemoveNode(item.Id))
        {
            return;
        }

        CommitStructuralChange();
    }

    /// <summary>
    /// Opens the rename box for a row. Folders have no other way to be named —
    /// the star's form only covers bookmarks — so this is how a folder created
    /// from the context menu stops being called "New folder".
    /// </summary>
    [RelayCommand]
    private void BeginRename(BookmarkNodeViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        CloseAllFolders();
        RenameTarget = item;
        RenameText = item.Name;
    }

    [RelayCommand]
    private void CommitRename()
    {
        if (RenameTarget is { } target)
        {
            Rename(target.Id, RenameText);
        }

        RenameTarget = null;
    }

    [RelayCommand]
    private void CancelRename() => RenameTarget = null;

    /// <summary>The row being renamed, or null when the box is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenaming))]
    private BookmarkNodeViewModel? _renameTarget;

    public bool IsRenaming => RenameTarget is not null;

    [ObservableProperty]
    private string _renameText = string.Empty;

    /// <summary>
    /// Adds a folder at the end of the bar, and opens its name for editing —
    /// a folder called "New folder" is of no use until it is named.
    /// </summary>
    [RelayCommand]
    private void CreateFolder()
    {
        var folder = _tree.AddFolder(_tree.Root, "New folder");
        CommitStructuralChange();
        BeginRenameOf(folder.Id);
    }

    /// <summary>Adds a folder inside an open folder's dropdown.</summary>
    [RelayCommand]
    private void CreateFolderIn(BookmarkNodeViewModel? parent)
    {
        var destination = parent is null ? _tree.Root : _tree.FindFolder(parent.Id) ?? _tree.Root;
        var folder = _tree.AddFolder(destination, "New folder");
        CommitStructuralChange();
        BeginRenameOf(folder.Id);
    }

    private void BeginRenameOf(BookmarkNodeId id)
    {
        if (FindRow(id, BarItems) is { } row)
        {
            RenameTarget = row;
            RenameText = row.Name;
        }
    }

    private static BookmarkNodeViewModel? FindRow(
        BookmarkNodeId id,
        IEnumerable<BookmarkNodeViewModel> rows)
    {
        foreach (var row in rows)
        {
            if (row.Id == id)
            {
                return row;
            }

            if (row.Children is { } children && FindRow(id, children) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    public Bookmark AddBookmark(
        BookmarkFolder parent,
        string name,
        PageAddress address,
        PageAddress? faviconAddress = null)
    {
        var bookmark = _tree.AddBookmark(parent, name, address, faviconAddress);
        CommitStructuralChange();
        return bookmark;
    }

    public BookmarkFolder AddFolder(BookmarkFolder parent, string name)
    {
        var folder = _tree.AddFolder(parent, name);
        CommitStructuralChange();
        return folder;
    }

    public void Rename(BookmarkNodeId id, string name)
    {
        if (_tree.RenameNode(id, name))
        {
            CommitStructuralChange();
        }
    }

    public void Remove(BookmarkNodeId id)
    {
        if (_tree.RemoveNode(id))
        {
            CommitStructuralChange();
        }
    }

    /// <summary>Where a drag ends up. Refused moves leave the tree untouched.</summary>
    public void Move(BookmarkNodeId id, BookmarkFolder newParent, BookmarkNodeId? before = null)
    {
        if (_tree.MoveNode(id, newParent, before))
        {
            CommitStructuralChange();
        }
    }

    /// <summary>
    /// The folders <paramref name="node"/> could be moved into: every folder in
    /// the tree except the one it is already in, and — for a folder — itself and
    /// its own descendants, which the tree would refuse anyway.
    /// </summary>
    public IReadOnlyList<BookmarkMoveTargetViewModel> BuildMoveTargets(BookmarkNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var targets = new List<BookmarkMoveTargetViewModel>();
        var current = _tree.ParentOf(node.Id);
        var moving = _tree.FindNode(node.Id) as BookmarkFolder;

        Add(_tree.Root, 0);
        return targets;

        void Add(BookmarkFolder folder, int depth)
        {
            var excluded = ReferenceEquals(folder, current) ||
                (moving is not null && (ReferenceEquals(folder, moving) || IsBeneath(folder, moving)));

            if (!excluded)
            {
                targets.Add(new BookmarkMoveTargetViewModel(this, node, folder, depth));
            }

            foreach (var child in folder.Children.OfType<BookmarkFolder>())
            {
                Add(child, depth + 1);
            }
        }

        bool IsBeneath(BookmarkFolder candidate, BookmarkFolder ancestor)
        {
            for (var walk = _tree.ParentOf(candidate.Id); walk is not null; walk = _tree.ParentOf(walk.Id))
            {
                if (ReferenceEquals(walk, ancestor))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public Bookmark? FindByAddress(PageAddress? address) =>
        address is null ? null : _tree.FindByAddress(address);

    public bool IsBookmarked(PageAddress? address) => FindByAddress(address) is not null;

    /// <summary>Shuts every open folder dropdown, at any depth.</summary>
    public void CloseAllFolders()
    {
        foreach (var row in BarItems)
        {
            CloseFolders(row);
        }
    }

    private static void CloseFolders(BookmarkNodeViewModel row)
    {
        row.IsOpen = false;

        if (row.Children is null)
        {
            return;
        }

        foreach (var child in row.Children)
        {
            CloseFolders(child);
        }
    }

    /// <summary>
    /// Brings the rows back in line with the tree and writes it out. Saving on
    /// every change is affordable here in a way it would not be for tab layout:
    /// bookmarks change only when a user deliberately edits them.
    /// </summary>
    private void CommitStructuralChange()
    {
        SyncBarItems();
        _store.Save(Capture());
        BookmarksChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Reconciles the bar's rows against the root's children in place, so a row
    /// that survives keeps its identity, its loaded icon and its open state —
    /// the same approach <see cref="ShellViewModel"/> takes for tabs.
    /// </summary>
    private void SyncBarItems()
    {
        for (var index = 0; index < _tree.Root.Children.Count; index++)
        {
            var child = _tree.Root.Children[index];
            var existing = BarItems.FirstOrDefault(row => row.Id == child.Id);

            if (existing is null)
            {
                BarItems.Insert(index, new BookmarkNodeViewModel(child, this, _favicons));
                continue;
            }

            if (BarItems.IndexOf(existing) != index)
            {
                BarItems.Move(BarItems.IndexOf(existing), index);
            }

            existing.Refresh();
        }

        while (BarItems.Count > _tree.Root.Children.Count)
        {
            BarItems.RemoveAt(BarItems.Count - 1);
        }
    }

    private BookmarkSnapshot Capture() => new(CaptureNode(_tree.Root));

    private static BookmarkNodeSnapshot CaptureNode(BookmarkNode node) => node switch
    {
        BookmarkFolder folder => new BookmarkNodeSnapshot(
            folder.Name,
            IsFolder: true,
            Children: folder.Children.Select(CaptureNode).ToList()),
        Bookmark bookmark => new BookmarkNodeSnapshot(
            bookmark.Name,
            IsFolder: false,
            bookmark.Address.ToString(),
            bookmark.FaviconAddress?.ToString()),
        _ => new BookmarkNodeSnapshot(node.Name, IsFolder: false),
    };

    /// <summary>
    /// Rebuilds a tree from a saved snapshot, or returns an empty one when there
    /// is nothing saved. Entries whose address no longer parses are dropped
    /// rather than failing the load — the rest of the bar is still worth having.
    /// </summary>
    public static BookmarkTree Restore(IBookmarkStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var tree = new BookmarkTree();

        if (store.Load() is not { } snapshot)
        {
            return tree;
        }

        if (snapshot.Root.Children is { } children)
        {
            RestoreChildren(tree, tree.Root, children);
        }

        return tree;
    }

    private static void RestoreChildren(
        BookmarkTree tree,
        BookmarkFolder parent,
        IReadOnlyList<BookmarkNodeSnapshot> children)
    {
        foreach (var child in children)
        {
            if (child.IsFolder)
            {
                var folder = tree.AddFolder(parent, child.Name);

                if (child.Children is { } nested)
                {
                    RestoreChildren(tree, folder, nested);
                }

                continue;
            }

            if (!TryParse(child.Address, out var address) || address is null)
            {
                continue;
            }

            // A missing or unparseable icon address is not a reason to drop the
            // bookmark: FaviconSource falls back to the site's own /favicon.ico.
            var favicon = TryParse(child.FaviconAddress, out var parsed) ? parsed : null;
            tree.AddBookmark(parent, child.Name, address, favicon);
        }
    }

    private static bool TryParse(string? value, out PageAddress? address)
    {
        address = null;

        return !string.IsNullOrWhiteSpace(value) &&
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            PageAddress.TryCreate(uri, out address);
    }
}
