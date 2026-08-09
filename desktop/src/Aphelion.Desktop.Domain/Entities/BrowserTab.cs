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

    public PageAddress? Address { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public TabLoadState LoadState { get; private set; } = TabLoadState.Idle;

    /// <summary>Reason the last navigation failed, or null when it did not.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// A tab that has never been navigated. The UI shows a new-tab page for these
    /// rather than an empty engine surface.
    /// </summary>
    public bool IsBlank => Address is null;

    /// <summary>Best available label for the tab strip.</summary>
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title) ? Title
        : Address is not null ? Address.DisplayHost
        : "New tab";

    public void BeginNavigation(PageAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        Address = address;
        LoadState = TabLoadState.Loading;
        FailureReason = null;

        // The old title belongs to the previous page; keeping it would mislabel
        // the tab for the whole load.
        Title = string.Empty;
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
}
