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

    public BookmarkFolder(BookmarkNodeId id, string name)
        : base(id, name)
    {
    }

    public IReadOnlyList<BookmarkNode> Children => _children;

    internal void InsertChild(int index, BookmarkNode node) =>
        _children.Insert(Math.Clamp(index, 0, _children.Count), node);

    internal bool RemoveChild(BookmarkNodeId id) => _children.RemoveAll(n => n.Id == id) > 0;

    internal int IndexOf(BookmarkNodeId id) => _children.FindIndex(n => n.Id == id);
}
