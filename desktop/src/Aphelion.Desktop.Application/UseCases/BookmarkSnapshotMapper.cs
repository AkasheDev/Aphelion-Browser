using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Application.UseCases;

/// <summary>The rebuilt bookmark aggregate and whether its stored form needed repair.</summary>
public sealed record BookmarkRestoreResult(BookmarkTree Tree, bool WasRepaired);

/// <summary>
/// Maps the bookmark domain aggregate to its persistence DTO and repairs older
/// or malformed snapshots while rebuilding it.
/// </summary>
/// <remarks>
/// Keeping migration here prevents the presentation layer from deciding JSON
/// compatibility, durable-id normalization, or tree invariants. The mapper is
/// deliberately storage-agnostic; the caller decides when and where to save the
/// normalized snapshot.
/// </remarks>
public static class BookmarkSnapshotMapper
{
    public static BookmarkSnapshot Capture(BookmarkTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return new BookmarkSnapshot(CaptureNode(tree.Root));
    }

    public static BookmarkNodeSnapshot CaptureNode(BookmarkNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node switch
        {
            BookmarkFolder folder => new BookmarkNodeSnapshot(
                folder.Name,
                IsFolder: true,
                Children: folder.Children.Select(CaptureNode).ToList(),
                GroupColor: folder.GroupColor?.ToString(),
                SavedGroupId: folder.SavedGroupId?.ToString()),
            Bookmark bookmark => new BookmarkNodeSnapshot(
                bookmark.Name,
                IsFolder: false,
                bookmark.Address.ToString(),
                bookmark.FaviconAddress?.ToString()),
            // A New Tab slot is a page with no address, which the snapshot can
            // already express. Restore reads it back as one only inside a saved
            // group; anywhere else an addressless page is still malformed.
            BlankPage blank => new BookmarkNodeSnapshot(blank.Name, IsFolder: false),
            _ => new BookmarkNodeSnapshot(node.Name, IsFolder: false),
        };
    }

    public static BookmarkRestoreResult Restore(BookmarkSnapshot? snapshot)
    {
        var tree = new BookmarkTree();

        if (snapshot?.Root?.Children is not { } children)
        {
            return new BookmarkRestoreResult(tree, WasRepaired: false);
        }

        var context = new RestoreContext(tree);
        RestoreChildren(tree, tree.Root, children, context: context);
        return new BookmarkRestoreResult(tree, context.WasRepaired);
    }

    /// <summary>Restores one clipboard node at a stable position in an existing tree.</summary>
    public static void RestoreNode(
        BookmarkTree tree,
        BookmarkFolder parent,
        BookmarkNodeSnapshot payload,
        BookmarkNodeId? before = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(payload);

        var index = before is { } anchor
            ? parent.Children.ToList().FindIndex(child => child.Id == anchor)
            : -1;

        RestoreChildren(
            tree,
            parent,
            [payload],
            index < 0 ? null : index,
            new RestoreContext(tree));
    }

    private static void RestoreChildren(
        BookmarkTree tree,
        BookmarkFolder parent,
        IReadOnlyList<BookmarkNodeSnapshot> children,
        int? index = null,
        RestoreContext? context = null)
    {
        context ??= new RestoreContext(tree);

        foreach (var child in children)
        {
            if (child is null)
            {
                context.WasRepaired = true;
                continue;
            }

            if (child.IsFolder)
            {
                RestoreFolder(tree, parent, child, index, context);
                index = index is { } at ? at + 1 : null;
                continue;
            }

            if (!TryParseAddress(child.Address, out var address) || address is null)
            {
                // Inside a saved group an addressless page is a recorded New Tab
                // rather than damage, and restoring it keeps the group the size
                // it was saved at. Only a stated-but-unreadable address is a
                // repair; a page that never had one is not.
                if (parent.IsSavedGroup && string.IsNullOrWhiteSpace(child.Address))
                {
                    tree.AddBlankPage(parent, child.Name, index);
                    index = index is { } slot ? slot + 1 : null;
                    continue;
                }

                context.WasRepaired = true;
                continue;
            }

            var hasFavicon = TryParseAddress(child.FaviconAddress, out var parsedIcon);

            if (!hasFavicon && !string.IsNullOrWhiteSpace(child.FaviconAddress))
            {
                context.WasRepaired = true;
            }

            tree.AddBookmark(parent, child.Name, address, hasFavicon ? parsedIcon : null, index);
            index = index is { } next ? next + 1 : null;
        }
    }

    private static void RestoreFolder(
        BookmarkTree tree,
        BookmarkFolder parent,
        BookmarkNodeSnapshot child,
        int? index,
        RestoreContext context)
    {
        var hasColorMarker = !string.IsNullOrWhiteSpace(child.GroupColor);
        var hasIdentityMarker = !string.IsNullOrWhiteSpace(child.SavedGroupId);
        var hasValidColor = Enum.TryParse<GroupColor>(child.GroupColor, out var parsedColor) &&
            Enum.IsDefined(parsedColor);
        var color = hasValidColor
            ? parsedColor
            : hasColorMarker || hasIdentityMarker
                ? GroupColor.Slate
                : (GroupColor?)null;
        var savedGroupId = SavedTabGroupId.TryParse(child.SavedGroupId, out var parsedId)
            ? parsedId
            : (SavedTabGroupId?)null;

        if (!hasValidColor && (hasColorMarker || hasIdentityMarker))
        {
            context.WasRepaired = true;
        }

        if (savedGroupId is { } duplicate && !context.UsedGroupIds.Add(duplicate))
        {
            savedGroupId = NewUniqueSavedGroupId(context.UsedGroupIds);
            context.WasRepaired = true;
        }
        else if (savedGroupId is null && hasIdentityMarker)
        {
            savedGroupId = NewUniqueSavedGroupId(context.UsedGroupIds);
            context.WasRepaired = true;
        }

        var isSavedGroup = color is not null || savedGroupId is not null;

        if (isSavedGroup && savedGroupId is null)
        {
            savedGroupId = NewUniqueSavedGroupId(context.UsedGroupIds);
            context.WasRepaired = true;
        }

        // Old builds allowed normal folders below a saved group. Preserve their
        // pages by flattening the invalid container into the saved group.
        if (parent.IsSavedGroup && !isSavedGroup)
        {
            context.WasRepaired = true;

            if (child.Children is { } flattened)
            {
                RestoreChildren(tree, parent, flattened, index, context);
            }

            return;
        }

        var destination = isSavedGroup ? tree.Root : parent;

        if (isSavedGroup && !ReferenceEquals(parent, tree.Root))
        {
            context.WasRepaired = true;
        }

        var destinationIndex = isSavedGroup
            ? tree.Root.Children.TakeWhile(node => node is BookmarkFolder { IsSavedGroup: true }).Count()
            : index;
        var folder = tree.AddFolder(
            destination,
            child.Name,
            destinationIndex,
            groupColor: color,
            savedGroupId: savedGroupId);

        if (child.Children is { } nested)
        {
            RestoreChildren(tree, folder, nested, context: context);
        }
    }

    private static bool TryParseAddress(string? value, out PageAddress? address)
    {
        address = null;
        return !string.IsNullOrWhiteSpace(value) &&
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            PageAddress.TryCreate(uri, out address);
    }

    private static SavedTabGroupId NewUniqueSavedGroupId(HashSet<SavedTabGroupId> used)
    {
        SavedTabGroupId created;

        do
        {
            created = SavedTabGroupId.New();
        }
        while (!used.Add(created));

        return created;
    }

    private sealed class RestoreContext
    {
        public RestoreContext(BookmarkTree tree)
        {
            UsedGroupIds = tree.SavedGroups
                .Select(folder => folder.SavedGroupId)
                .OfType<SavedTabGroupId>()
                .ToHashSet();
        }

        public HashSet<SavedTabGroupId> UsedGroupIds { get; }

        public bool WasRepaired { get; set; }
    }
}
