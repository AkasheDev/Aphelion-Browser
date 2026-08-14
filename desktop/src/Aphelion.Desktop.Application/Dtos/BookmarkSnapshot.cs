namespace Aphelion.Desktop.Application.Dtos;

/// <summary>One bookmark or folder as persisted, with its subtree.</summary>
/// <remarks>
/// Nested rather than flattened: unlike the tab session, which records a
/// partner by index because JSON cannot hold a reference, a bookmark tree maps
/// straight onto nested arrays and needs no such indirection.
/// </remarks>
public sealed record BookmarkNodeSnapshot(
    string Name,
    bool IsFolder,
    string? Address = null,
    string? FaviconAddress = null,
    IReadOnlyList<BookmarkNodeSnapshot>? Children = null,
    /// <summary>
    /// Set when the folder is a saved tab group, naming the colour it is drawn
    /// in. Absent on ordinary folders, so files written before saved groups
    /// existed still read back correctly.
    /// </summary>
    string? GroupColor = null,
    /// <summary>
    /// Durable saved-tab-group identity in compact GUID form. Optional so
    /// bookmark files written before durable group identities still deserialize.
    /// </summary>
    string? SavedGroupId = null);

/// <summary>The saved bookmark hierarchy.</summary>
public sealed record BookmarkSnapshot(BookmarkNodeSnapshot Root);
