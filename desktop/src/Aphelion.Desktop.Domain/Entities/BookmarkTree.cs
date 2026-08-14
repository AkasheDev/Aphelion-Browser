using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Domain.Entities;

/// <summary>
/// The whole bookmark hierarchy, and the only thing that changes its shape.
/// </summary>
/// <remarks>
/// The bar's top level is the root folder's children rather than a separate
/// list, so adding, removing and moving work the same at every depth instead of
/// special-casing the top.
/// </remarks>
public sealed class BookmarkTree
{
    private readonly Dictionary<BookmarkNodeId, BookmarkFolder> _parentOf = [];

    /// <summary>The bar itself. Never has a parent and is never moved or removed.</summary>
    public BookmarkFolder Root { get; } = new(BookmarkNodeId.New(), "Bookmarks bar");

    public Bookmark AddBookmark(
        BookmarkFolder parent,
        string name,
        PageAddress address,
        PageAddress? faviconAddress = null,
        int? index = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(address);

        if (!ContainsFolder(parent))
        {
            throw new ArgumentException("The destination folder does not belong to this bookmark tree.", nameof(parent));
        }

        var bookmark = new Bookmark(BookmarkNodeId.New(), name, address, faviconAddress);
        Attach(parent, bookmark, index);
        return bookmark;
    }

    /// <summary>
    /// Records a New Tab slot inside a saved group. Only saved groups reopen
    /// their children as tabs, so only they have anywhere to put one.
    /// </summary>
    public BlankPage AddBlankPage(BookmarkFolder parent, string name = "New tab", int? index = null)
    {
        ArgumentNullException.ThrowIfNull(parent);

        if (!ContainsFolder(parent))
        {
            throw new ArgumentException("The destination folder does not belong to this bookmark tree.", nameof(parent));
        }

        if (!parent.IsSavedGroup)
        {
            throw new ArgumentException("A New Tab slot belongs only to a saved tab group.", nameof(parent));
        }

        var blank = new BlankPage(BookmarkNodeId.New(), name);
        Attach(parent, blank, index);
        return blank;
    }

    /// <summary>
    /// The pages of a saved group in order, New Tab slots included. Saved groups
    /// hold only pages, so this is every child of one.
    /// </summary>
    public static IReadOnlyList<BookmarkNode> PagesOf(BookmarkFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return folder.Children.Where(child => child is Bookmark or BlankPage).ToList();
    }

    /// <summary>The address a saved-group page reopens at, or null for a New Tab.</summary>
    public static PageAddress? AddressOf(BookmarkNode page) => (page as Bookmark)?.Address;

    public BookmarkFolder AddFolder(
        BookmarkFolder parent,
        string name,
        int? index = null,
        GroupColor? groupColor = null,
        SavedTabGroupId? savedGroupId = null)
    {
        ArgumentNullException.ThrowIfNull(parent);

        if (!ContainsFolder(parent))
        {
            throw new ArgumentException("The destination folder does not belong to this bookmark tree.", nameof(parent));
        }

        var isSavedGroup = groupColor is not null || savedGroupId is not null;

        if ((isSavedGroup && !ReferenceEquals(parent, Root)) ||
            (!isSavedGroup && parent.IsSavedGroup))
        {
            throw new InvalidOperationException(
                "Saved tab groups are flat, top-level bookmark-bar records.");
        }

        var folder = new BookmarkFolder(BookmarkNodeId.New(), name, groupColor, savedGroupId);
        Attach(parent, folder, index);
        return folder;
    }

    /// <summary>The saved tab groups, which sit among the bar's top-level rows.</summary>
    public IEnumerable<BookmarkFolder> SavedGroups =>
        Root.Children.OfType<BookmarkFolder>().Where(folder => folder.IsSavedGroup);

    /// <summary>Removes a node and, for a folder, everything beneath it.</summary>
    public bool RemoveNode(BookmarkNodeId id)
    {
        if (!_parentOf.TryGetValue(id, out var parent))
        {
            return false;
        }

        var node = parent.Children.FirstOrDefault(n => n.Id == id);

        if (node is null)
        {
            return false;
        }

        Forget(node);
        parent.RemoveChild(id);
        return true;
    }

    public bool RenameNode(BookmarkNodeId id, string name)
    {
        if (FindNode(id) is not { } node)
        {
            return false;
        }

        node.Rename(name);
        return true;
    }

    /// <summary>
    /// Moves a node under <paramref name="newParent"/>, positioned before
    /// <paramref name="before"/> or last when that is null.
    /// </summary>
    /// <remarks>
    /// Moving a folder into itself or into its own descendant would detach that
    /// subtree from the root entirely, so it is refused rather than throwing —
    /// this is reached from a drag, where an impossible drop is a normal thing
    /// for a user to attempt and should simply not take effect.
    /// </remarks>
    public bool MoveNode(BookmarkNodeId id, BookmarkFolder newParent, BookmarkNodeId? before = null)
    {
        ArgumentNullException.ThrowIfNull(newParent);

        if (id == newParent.Id ||
            !ContainsFolder(newParent) ||
            !_parentOf.TryGetValue(id, out var currentParent) ||
            FindNode(id) is not { } node)
        {
            return false;
        }

        if (node is BookmarkFolder folder && IsAncestorOf(folder, newParent))
        {
            return false;
        }

        if (newParent.IsSavedGroup ||
            node is BookmarkFolder { IsSavedGroup: true } && !ReferenceEquals(newParent, Root))
        {
            return false;
        }

        var index = before is { } anchor ? newParent.IndexOf(anchor) : -1;

        if (ReferenceEquals(currentParent, newParent))
        {
            var from = newParent.IndexOf(id);

            if (from < 0)
            {
                return false;
            }

            // Taking the node out shifts everything after it down one, so an
            // anchor that sat later in the same folder has to shift with it.
            if (index > from)
            {
                index--;
            }

            newParent.RemoveChild(id);
            newParent.InsertChild(index < 0 ? newParent.Children.Count : index, node);
            return true;
        }

        currentParent.RemoveChild(id);
        newParent.InsertChild(index < 0 ? newParent.Children.Count : index, node);
        _parentOf[id] = newParent;
        return true;
    }

    public BookmarkNode? FindNode(BookmarkNodeId id) =>
        id == Root.Id
            ? Root
            : _parentOf.TryGetValue(id, out var parent)
                ? parent.Children.FirstOrDefault(n => n.Id == id)
                : null;

    public BookmarkFolder? FindFolder(BookmarkNodeId id) => FindNode(id) as BookmarkFolder;

    public BookmarkFolder? ParentOf(BookmarkNodeId id) =>
        _parentOf.TryGetValue(id, out var parent) ? parent : null;

    /// <summary>
    /// The first bookmark saved for this address, or null. Backs the toolbar
    /// star, which shows filled when the open page is already bookmarked.
    /// </summary>
    public Bookmark? FindByAddress(PageAddress address) =>
        address is null
            ? null
            : OrdinaryBookmarksBeneath(Root).FirstOrDefault(bookmark => bookmark.Address.Equals(address));

    /// <summary>
    /// Ordinary bookmarks below a folder, excluding saved-group records and
    /// everything beneath them. A saved group's pages describe its restorable
    /// tab set; they are not bookmarks chosen with the toolbar star.
    /// </summary>
    private static IEnumerable<Bookmark> OrdinaryBookmarksBeneath(BookmarkFolder folder)
    {
        foreach (var child in folder.Children)
        {
            if (child is Bookmark bookmark)
            {
                yield return bookmark;
                continue;
            }

            if (child is BookmarkFolder { IsSavedGroup: false } nested)
            {
                foreach (var descendant in OrdinaryBookmarksBeneath(nested))
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>Every node beneath <paramref name="folder"/>, depth first.</summary>
    public static IEnumerable<BookmarkNode> Descendants(BookmarkFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        foreach (var child in folder.Children)
        {
            yield return child;

            if (child is BookmarkFolder nested)
            {
                foreach (var descendant in Descendants(nested))
                {
                    yield return descendant;
                }
            }
        }
    }

    private void Attach(BookmarkFolder parent, BookmarkNode node, int? index)
    {
        parent.InsertChild(index ?? parent.Children.Count, node);
        _parentOf[node.Id] = parent;
    }

    private bool ContainsFolder(BookmarkFolder folder)
    {
        if (ReferenceEquals(folder, Root))
        {
            return true;
        }

        return _parentOf.TryGetValue(folder.Id, out var parent) &&
            parent.Children.Any(child => ReferenceEquals(child, folder));
    }

    private void Forget(BookmarkNode node)
    {
        _parentOf.Remove(node.Id);

        if (node is BookmarkFolder folder)
        {
            foreach (var descendant in Descendants(folder))
            {
                _parentOf.Remove(descendant.Id);
            }
        }
    }

    private bool IsAncestorOf(BookmarkFolder folder, BookmarkFolder candidate)
    {
        for (var walk = candidate; walk is not null; walk = ParentOf(walk.Id))
        {
            if (ReferenceEquals(walk, folder))
            {
                return true;
            }
        }

        return false;
    }
}
