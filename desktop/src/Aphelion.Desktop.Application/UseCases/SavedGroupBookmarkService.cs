using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Application.UseCases;

/// <summary>The saved row selected or created while mirroring a live tab group.</summary>
public sealed record SavedGroupMirrorResult(
    BookmarkFolder Folder,
    SavedTabGroupId SavedGroupId,
    bool Changed);

/// <summary>
/// Applies one live tab group's durable state to the bookmark aggregate.
/// </summary>
/// <remarks>
/// Matching, legacy adoption, page reconciliation, and metadata preservation are
/// application rules rather than Avalonia concerns. The presentation layer only
/// supplies which rows are already claimed by other windows and persists when
/// <see cref="SavedGroupMirrorResult.Changed"/> is true.
/// </remarks>
public static class SavedGroupBookmarkService
{
    public static SavedGroupMirrorResult Mirror(
        BookmarkTree tree,
        BookmarkFolder? linkedFolder,
        IReadOnlySet<BookmarkNodeId> claimedFolderIds,
        SavedTabGroupId proposedId,
        string name,
        GroupColor color,
        IReadOnlyList<BookmarkPageCandidate> pages,
        bool replaceWithEmpty = false,
        bool canAdoptIdentifiedExactMatch = false)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(claimedFolderIds);
        ArgumentNullException.ThrowIfNull(pages);

        var changed = false;
        var folder = linkedFolder is not null && tree.FindFolder(linkedFolder.Id) is not null
            ? linkedFolder
            : null;

        if (folder is null)
        {
            folder = tree.SavedGroups.FirstOrDefault(candidate =>
                candidate.SavedGroupId == proposedId);

            if (folder is null)
            {
                var exactUnclaimed = tree.SavedGroups
                    .Where(candidate =>
                        !claimedFolderIds.Contains(candidate.Id) &&
                        Matches(candidate, name, color, pages))
                    .ToList();

                // Consume old null-id rows one-to-one. Bookmark snapshots are
                // normalized before session restoration, so an old row may
                // already have a generated id; a legacy session run may adopt
                // that identified row in saved order as well.
                folder = exactUnclaimed.FirstOrDefault(candidate => candidate.SavedGroupId is null);

                if (folder is null &&
                    canAdoptIdentifiedExactMatch &&
                    exactUnclaimed.Count > 0)
                {
                    folder = exactUnclaimed[0];
                }
            }

            if (folder is null)
            {
                folder = tree.AddFolder(
                    tree.Root,
                    name,
                    index: SavedGroupCount(tree),
                    groupColor: color,
                    savedGroupId: proposedId);
                changed = true;
            }
        }

        if (folder.SavedGroupId is null)
        {
            folder.MarkAsGroup(color, proposedId);
            changed = true;
        }

        if (!string.Equals(folder.Name, name, StringComparison.Ordinal))
        {
            folder.Rename(name);
            changed = true;
        }

        if (folder.GroupColor != color)
        {
            folder.MarkAsGroup(color);
            changed = true;
        }

        var existing = BookmarkTree.PagesOf(folder);
        var sameAddresses = SameAddresses(existing, pages);

        if (!sameAddresses && (pages.Count > 0 || replaceWithEmpty))
        {
            foreach (var page in existing)
            {
                tree.RemoveNode(page.Id);
            }

            foreach (var page in pages)
            {
                if (page.Address is { } address)
                {
                    tree.AddBookmark(folder, page.Title, address, page.FaviconAddress);
                }
                else
                {
                    tree.AddBlankPage(folder, page.Title);
                }
            }

            changed = true;
        }
        else if (sameAddresses)
        {
            foreach (var (saved, page) in existing.Zip(pages))
            {
                if (page.HasResolvedTitle &&
                    !string.Equals(saved.Name, page.Title, StringComparison.Ordinal))
                {
                    saved.Rename(page.Title);
                    changed = true;
                }

                // Only a real page carries an icon; a New Tab slot has none.
                if (page.HasResolvedFavicon &&
                    saved is Bookmark bookmark &&
                    bookmark.FaviconAddress != page.FaviconAddress)
                {
                    bookmark.UpdateAddress(bookmark.Address, page.FaviconAddress);
                    changed = true;
                }
            }
        }

        return new SavedGroupMirrorResult(
            folder,
            folder.SavedGroupId ?? proposedId,
            changed);
    }

    private static bool Matches(
        BookmarkFolder folder,
        string name,
        GroupColor color,
        IReadOnlyList<BookmarkPageCandidate> pages)
    {
        if (!string.Equals(folder.Name, name, StringComparison.Ordinal) ||
            folder.GroupColor != color)
        {
            return false;
        }

        return SameAddresses(BookmarkTree.PagesOf(folder), pages);
    }

    /// <summary>
    /// Whether a group's stored pages already line up with the live ones, slot
    /// for slot. A New Tab matches only another New Tab, so a group does not
    /// look unchanged when a blank slot has become a real page or the reverse.
    /// </summary>
    private static bool SameAddresses(
        IReadOnlyList<BookmarkNode> saved,
        IReadOnlyList<BookmarkPageCandidate> pages)
    {
        if (saved.Count != pages.Count)
        {
            return false;
        }

        return saved.Zip(pages).All(pair =>
            BookmarkTree.AddressOf(pair.First) is { } address
                ? address.Equals(pair.Second.Address)
                : pair.Second.Address is null);
    }

    private static int SavedGroupCount(BookmarkTree tree) =>
        tree.Root.Children
            .TakeWhile(child => child is BookmarkFolder { IsSavedGroup: true })
            .Count();
}
