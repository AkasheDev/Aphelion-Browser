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

    /// <summary>
    /// Applies a page scale without changing its navigation state and returns
    /// the factor the engine actually accepted.
    /// </summary>
    double SetZoomFactor(double factor);

    void Navigate(PageAddress address);

    bool GoBack();

    bool GoForward();

    bool Reload();

    bool StopLoading();

    /// <summary>
    /// Removes every cookie this session's engine surface holds.
    /// </summary>
    /// <remarks>
    /// The host platform's web view has no notion of a separate private profile —
    /// see the remarks on <c>NativeWebViewSession</c> for why — so a private
    /// window's isolation is enforced by clearing state rather than by never
    /// sharing it. Best effort: a page that never loaded a session has nothing to
    /// clear, and a failure here must not stop the window from closing.
    /// </remarks>
    Task ClearBrowsingDataAsync();

    /// <summary>
    /// Runs a script in the page and returns its result as text.
    /// </summary>
    /// <remarks>
    /// The engine reports no page title or favicon of its own, so both are read
    /// from the document through this. Returns null when the engine is not ready
    /// or the script fails.
    /// </remarks>
    Task<string?> EvaluateAsync(string script);

    /// <summary>Raised when a navigation begins, whatever started it.</summary>
    event EventHandler<EngineNavigationStartedEventArgs>? NavigationStarted;

    /// <summary>Raised when a navigation finishes, successfully or not.</summary>
    event EventHandler<EngineNavigationCompletedEventArgs>? NavigationCompleted;

    /// <summary>
    /// Raised when the engine itself changes zoom, including native Ctrl+wheel
    /// and native keyboard zoom handled inside the hosted page.
    /// </summary>
    event EventHandler<EngineZoomFactorChangedEventArgs>? ZoomFactorChanged;

    /// <summary>
    /// Raised when the page starts downloading a file, however it was started —
    /// a clicked link, a script, or a navigation the engine turned into a
    /// download. The operation in the args is live and controllable.
    /// </summary>
    /// <remarks>
    /// Only raised where the platform engine exposes its download pipeline —
    /// WebView2 on Windows today. On the other hosts downloads fall back to the
    /// platform's own handling until their adapters gain the same bridge, as
    /// anticipated by ADR-0001's download-handling follow-up.
    /// </remarks>
    event EventHandler<EngineDownloadStartedEventArgs>? DownloadStarted;
}

public sealed class EngineDownloadStartedEventArgs(IEngineDownloadOperation operation) : EventArgs
{
    public IEngineDownloadOperation Operation { get; } = operation;
}

public sealed class EngineZoomFactorChangedEventArgs(double factor) : EventArgs
{
    public double Factor { get; } = factor;
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

public sealed class EngineNavigationCompletedEventArgs(
    bool isSuccess,
    string? failureReason,
    Uri? requestedUrl = null) : EventArgs
{
    public bool IsSuccess { get; } = isSuccess;

    public string? FailureReason { get; } = failureReason;

    /// <summary>
    /// Document address reported by the engine for this completion. Correlating
    /// it with the domain tab prevents a reused native surface from completing a
    /// previous tab's navigation on its new owner.
    /// </summary>
    public Uri? RequestedUrl { get; } = requestedUrl;
}
