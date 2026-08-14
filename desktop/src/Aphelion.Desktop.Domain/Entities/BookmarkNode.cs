using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Domain.Entities;

/// <summary>
/// An entry in the bookmark tree: either a <see cref="Bookmark"/> or a
/// <see cref="BookmarkFolder"/>.
/// </summary>
public abstract class BookmarkNode
{
    protected BookmarkNode(BookmarkNodeId id, string name)
    {
        Id = id;
        Rename(name);
    }

    public BookmarkNodeId Id { get; }

    public string Name { get; private set; } = string.Empty;

    public void Rename(string name) =>
        Name = string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Trim();
}

/// <summary>A saved page.</summary>
public sealed class Bookmark : BookmarkNode
{
    public Bookmark(
        BookmarkNodeId id,
        string name,
        PageAddress address,
        PageAddress? faviconAddress = null)
        : base(id, name)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));
        FaviconAddress = faviconAddress;
    }

    public PageAddress Address { get; private set; }

    /// <summary>
    /// Where the icon was found when the bookmark was made, when that is known.
    /// </summary>
    /// <remarks>
    /// Kept so a bookmark does not have to be visited again to draw its icon.
    /// When it is absent <see cref="FaviconSource"/> falls back to the site's
    /// conventional location.
    /// </remarks>
    public PageAddress? FaviconAddress { get; private set; }

    /// <summary>
    /// The address to fetch this bookmark's icon from: the one recorded with it,
    /// or the site's /favicon.ico when none was.
    /// </summary>
    public Uri FaviconSource =>
        FaviconAddress?.Value ?? new Uri($"{Address.Value.Scheme}://{Address.Value.Host}/favicon.ico");

    public void UpdateAddress(PageAddress address, PageAddress? faviconAddress)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));
        FaviconAddress = faviconAddress;
    }
}

/// <summary>A New Tab recorded inside a saved tab group.</summary>
/// <remarks>
/// The New Tab page is a surface this application draws rather than somewhere
/// the browser navigates, so it has no address a <see cref="Bookmark"/> could
/// hold — and <see cref="PageAddress"/> deliberately admits only http and https,
/// so inventing an internal scheme for it is not an option. It still occupies a
/// slot in the group, and without one a group reopened smaller than it was
/// closed. Being its own type keeps it clear of the <c>OfType&lt;Bookmark&gt;</c>
/// filters that drive the bar, the star, and folder contents: a New Tab slot is
/// only ever meaningful inside the group holding it.
/// </remarks>
public sealed class BlankPage : BookmarkNode
{
    public BlankPage(BookmarkNodeId id, string name = "New tab")
        : base(id, name)
    {
    }
}

/// <summary>A named container of bookmarks and further folders.</summary>
/// <remarks>
/// Structural changes go through <see cref="BookmarkTree"/> rather than being
/// invoked on a folder directly — hence the internal mutators. The tree keeps a
/// parent lookup alongside the nodes, and a folder rearranging itself behind the
/// tree's back would leave that lookup describing a shape that no longer exists.
/// </remarks>
public sealed class BookmarkFolder : BookmarkNode
{
    private readonly List<BookmarkNode> _children = [];

    public BookmarkFolder(
        BookmarkNodeId id,
        string name,
        GroupColor? groupColor = null,
        SavedTabGroupId? savedGroupId = null)
        : base(id, name)
    {
        GroupColor = groupColor;
        SavedGroupId = savedGroupId;
    }

    /// <summary>
    /// Set when this folder is a saved tab group rather than an ordinary folder,
    /// and holds the colour that group is drawn in.
    /// </summary>
    /// <remarks>
    /// A saved group reuses a folder's ordered children for persistence, but
    /// <see cref="BookmarkTree"/> keeps it as a flat top-level record. Its colour
    /// and durable id express that its pages reopen together as a tab group.
    /// </remarks>
    public GroupColor? GroupColor { get; private set; }

    /// <summary>
    /// Durable identity shared by this record and any live tab-group occurrence
    /// reopened from it. Null on ordinary folders and legacy saved groups that
    /// have not yet been upgraded.
    /// </summary>
    public SavedTabGroupId? SavedGroupId { get; private set; }

    public bool IsSavedGroup => GroupColor is not null || SavedGroupId is not null;

    public void MarkAsGroup(GroupColor color, SavedTabGroupId? savedGroupId = null)
    {
        GroupColor = color;

        if (savedGroupId is not null)
        {
            SavedGroupId = savedGroupId;
        }
    }

    public void ClearGroup()
    {
        GroupColor = null;
        SavedGroupId = null;
    }

    public IReadOnlyList<BookmarkNode> Children => _children;

    internal void InsertChild(int index, BookmarkNode node) =>
        _children.Insert(Math.Clamp(index, 0, _children.Count), node);

    internal bool RemoveChild(BookmarkNodeId id) => _children.RemoveAll(n => n.Id == id) > 0;

    internal int IndexOf(BookmarkNodeId id) => _children.FindIndex(n => n.Id == id);
}
