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
    IReadOnlyList<BookmarkNodeSnapshot>? Children = null);

/// <summary>The saved bookmark hierarchy.</summary>
public sealed record BookmarkSnapshot(BookmarkNodeSnapshot Root);
