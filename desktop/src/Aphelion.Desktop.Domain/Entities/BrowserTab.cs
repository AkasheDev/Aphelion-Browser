using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Domain.Entities;

public enum TabLoadState
{
    Idle,
    Loading,
    Failed,
}

/// <summary>
/// One browser tab. Holds the state of a tab and the rules governing how that
/// state may change; it does not render anything and knows nothing about the
/// engine that does.
/// </summary>
public sealed class BrowserTab
{
    public BrowserTab(TabId id, PageAddress? initialAddress = null)
    {
        Id = id;
        Address = initialAddress;
    }

    public TabId Id { get; }

    /// <summary>The group this tab belongs to, or null when it is ungrouped.</summary>
    public TabGroupId? GroupId { get; private set; }

    public PageAddress? Address { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// Address of the page's favicon, when it reported one.
    /// </summary>
    public PageAddress? FaviconAddress { get; private set; }

    /// <summary>
    /// What the user typed to reach this page, when they searched rather than
    /// navigated. A search result page's own title is the engine's wording; the
    /// query is what the user recognises, so the strip shows that instead.
    /// </summary>
    public string? SearchTerm { get; private set; }

    public TabLoadState LoadState { get; private set; } = TabLoadState.Idle;

    /// <summary>Reason the last navigation failed, or null when it did not.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// A tab that has never been navigated. The UI shows a new-tab page for these
    /// rather than an empty engine surface.
    /// </summary>
    public bool IsBlank => Address is null;

    /// <summary>
    /// Best available label for the tab strip: the search term for a results page,
    /// otherwise the page's own title, falling back to the host while it loads.
    /// </summary>
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(SearchTerm) ? SearchTerm!
        : !string.IsNullOrWhiteSpace(Title) ? Title
        : Address is not null ? Address.DisplayHost
        : "New tab";

    /// <summary>
    /// Starts a navigation. <paramref name="searchTerm"/> is set when the user
    /// searched rather than typed an address, and becomes the tab's label.
    /// </summary>
    public void BeginNavigation(PageAddress address, string? searchTerm = null)
    {
        ArgumentNullException.ThrowIfNull(address);

        Address = address;
        LoadState = TabLoadState.Loading;
        FailureReason = null;
        SearchTerm = string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm;

        // The old title and icon belong to the previous page; keeping them would
        // mislabel the tab for the whole load.
        Title = string.Empty;
        FaviconAddress = null;
    }

    public void CompleteNavigation(string? title)
    {
        LoadState = TabLoadState.Idle;
        FailureReason = null;

        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title;
        }
    }

    public void FailNavigation(string? reason)
    {
        LoadState = TabLoadState.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Navigation failed." : reason;
    }

    public void UpdateTitle(string? title)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title;
        }
    }

    public void UpdateFavicon(PageAddress? address) => FaviconAddress = address;

    public void JoinGroup(TabGroupId groupId) => GroupId = groupId;

    public void LeaveGroup() => GroupId = null;
}
