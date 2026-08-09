using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Application.Ports;

/// <summary>
/// Control surface for one rendering engine instance backing a single tab.
/// </summary>
/// <remarks>
/// This is the boundary described in ADR-0001. Nothing above this interface knows
/// which engine renders the page, so the engine can be replaced by writing a new
/// adapter rather than by touching application or domain code.
/// </remarks>
public interface IBrowserEngineSession
{
    bool CanGoBack { get; }

    bool CanGoForward { get; }

    void Navigate(PageAddress address);

    bool GoBack();

    bool GoForward();

    bool Reload();

    bool StopLoading();

    /// <summary>Raised when a navigation begins, whatever started it.</summary>
    event EventHandler<EngineNavigationStartedEventArgs>? NavigationStarted;

    /// <summary>Raised when a navigation finishes, successfully or not.</summary>
    event EventHandler<EngineNavigationCompletedEventArgs>? NavigationCompleted;
}

public sealed class EngineNavigationStartedEventArgs(Uri? requestedUrl) : EventArgs
{
    /// <summary>
    /// Raw URL from the engine. Not a <see cref="PageAddress"/>: a page can start
    /// navigating to schemes the domain refuses, and the application layer decides
    /// what to do about that.
    /// </summary>
    public Uri? RequestedUrl { get; } = requestedUrl;
}

public sealed class EngineNavigationCompletedEventArgs(bool isSuccess, string? failureReason) : EventArgs
{
    public bool IsSuccess { get; } = isSuccess;

    public string? FailureReason { get; } = failureReason;
}
