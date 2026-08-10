namespace Aphelion.Desktop.Application.Dtos;

/// <summary>One open tab as persisted between runs.</summary>
public sealed record SessionTabSnapshot(
    string? Address,
    string? GroupName,
    string? GroupColor,
    bool GroupCollapsed,
    int? SplitPartnerIndex = null);

/// <summary>
/// The open-tab state persisted between runs so the browser reopens where the
/// user left off. Deliberately not the history log: this is "what is open now",
/// which exists independently of any record of past visits.
/// </summary>
public sealed record SessionSnapshot(
    IReadOnlyList<SessionTabSnapshot> Tabs,
    int ActiveIndex);
