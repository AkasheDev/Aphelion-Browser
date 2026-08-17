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

    /// <summary>
    /// The tab shown beside this one in split view, or null when not split.
    /// </summary>
    /// <remarks>
    /// A split pair is one entry everywhere the user sees a list of tabs: the
    /// strip, the overflow panel, the picker. The left tab holds the pairing and
    /// is the one that appears; its partner is hidden from those lists while the
    /// split lasts.
    /// </remarks>
    public TabId? SplitPartnerId { get; private set; }

    /// <summary>True when this tab is the hidden right half of a split pair.</summary>
    public bool IsSplitPartner { get; private set; }

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
    /// The local downloads page, drawn by the application rather than the
    /// engine. Distinct from a blank New Tab: this tab has a name and an icon
    /// of its own, and it is not an empty surface waiting for an address.
    /// </summary>
    public bool IsDownloadsPage { get; private set; }

    /// <summary>
    /// A tab that has never been navigated. The UI shows a new-tab page for these
    /// rather than an empty engine surface.
    /// </summary>
    public bool IsBlank => Address is null && !IsDownloadsPage;

    /// <summary>
    /// Best available label for the tab strip: the search term for a results page,
    /// otherwise the page's own title, falling back to the host while it loads.
    /// </summary>
    public string DisplayTitle =>
        IsDownloadsPage ? InternalPages.DownloadsTitle
        : IsBlank ? "New tab"
        : !string.IsNullOrWhiteSpace(SearchTerm) ? SearchTerm!
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

        IsDownloadsPage = false;
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

    /// <summary>
    /// Aborts an in-flight load. The document already on screen stays; chrome
    /// returns to idle so reload is available again. Not a failure: the user
    /// asked to stop, the same way Chrome's X does.
    /// </summary>
    public void CancelNavigation()
    {
        if (LoadState != TabLoadState.Loading)
        {
            return;
        }

        LoadState = TabLoadState.Idle;
        FailureReason = null;
    }

    public void FailNavigation(string? reason)
    {
        LoadState = TabLoadState.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Navigation failed." : reason;
    }

    /// <summary>Opens the local downloads page in this tab.</summary>
    public void ShowDownloads()
    {
        Address = null;
        IsDownloadsPage = true;
        Title = InternalPages.DownloadsTitle;
        FaviconAddress = null;
        SearchTerm = null;
        LoadState = TabLoadState.Idle;
        FailureReason = null;
    }

    /// <summary>Returns this tab to the local New Tab experience.</summary>
    public void ResetToBlank()
    {
        Address = null;
        IsDownloadsPage = false;
        Title = string.Empty;
        FaviconAddress = null;
        SearchTerm = null;
        LoadState = TabLoadState.Idle;
        FailureReason = null;
    }

    /// <summary>
    /// Returns a tab that stepped back to New Tab to the page it came from.
    /// </summary>
    /// <remarks>
    /// Stepping back to New Tab never moves the engine — New Tab is drawn over a
    /// surface that is still showing the page — so going forward again restores
    /// what the tab knew rather than navigating anywhere. Reloading instead would
    /// discard the page's scroll position and post state, and would push a second
    /// history entry for a page the engine never left.
    /// </remarks>
    public void RestoreFromBlank(
        PageAddress address,
        string? title,
        string? searchTerm,
        PageAddress? faviconAddress)
    {
        ArgumentNullException.ThrowIfNull(address);

        IsDownloadsPage = false;
        Address = address;
        Title = title ?? string.Empty;
        SearchTerm = string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm;
        FaviconAddress = faviconAddress;
        LoadState = TabLoadState.Idle;
        FailureReason = null;
    }

    public void UpdateTitle(string? title)
    {
        if (Address is not null && !string.IsNullOrWhiteSpace(title))
        {
            Title = title;
        }
    }

    public void UpdateFavicon(PageAddress? address) =>
        FaviconAddress = Address is null ? null : address;

    /// <summary>Pairs this tab with <paramref name="partner"/> as the left half.</summary>
    public void SplitWith(TabId partner) => SplitPartnerId = partner;

    /// <summary>Marks this tab as the hidden right half of a pair.</summary>
    public void BecomeSplitPartner() => IsSplitPartner = true;

    /// <summary>Breaks the pairing, whichever half this tab was.</summary>
    public void LeaveSplit()
    {
        SplitPartnerId = null;
        IsSplitPartner = false;
    }

    public void JoinGroup(TabGroupId groupId) => GroupId = groupId;

    public void LeaveGroup() => GroupId = null;
}
