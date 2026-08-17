namespace Aphelion.Desktop.Application.Dtos;

/// <summary>One open tab as persisted between runs.</summary>
public sealed record SessionTabSnapshot(
    string? Address,
    string? GroupName,
    string? GroupColor,
    bool GroupCollapsed,
    int? SplitPartnerIndex = null,
    /// <summary>
    /// Durable identity of the saved group this live tab group represents.
    /// Optional for compatibility with session files written before that link
    /// was persisted.
    /// </summary>
    string? SavedGroupId = null,
    bool IsPinned = false);

/// <summary>One ordinary window's tabs as persisted between runs.</summary>
public sealed record SessionWindowSnapshot(
    IReadOnlyList<SessionTabSnapshot> Tabs,
    int ActiveIndex,
    int Left = 0,
    int Top = 0,
    double Width = 0,
    double Height = 0);

/// <summary>
/// The open-tab state persisted between runs so the browser reopens where the
/// user left off. Deliberately not the history log: this is "what is open now",
/// which exists independently of any record of past visits.
/// </summary>
/// <remarks>
/// <see cref="Windows"/> is optional so session files written before multi-window
/// persistence still load: they are treated as a single window whose tabs are
/// <see cref="Tabs"/>. When <see cref="Windows"/> is present it is the source of
/// truth and <see cref="Tabs"/> repeats the first window for older readers.
/// </remarks>
public sealed record SessionSnapshot(
    IReadOnlyList<SessionTabSnapshot> Tabs,
    int ActiveIndex,
    IReadOnlyList<SessionWindowSnapshot>? Windows = null);
