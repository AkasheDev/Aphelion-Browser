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
    /// Tells the session whether its tab is the one on screen.
    /// </summary>
    /// <remarks>
    /// A hidden tab keeps its engine surface alive - see the comment in
    /// <c>BrowserView</c> about why it is not torn down - so without this every
    /// open tab goes on polling its page for events forever, four times a second
    /// each, on the one UI thread they all share. Only the tab being looked at
    /// can produce the hovers and clicks that polling is there to collect.
    /// </remarks>
    void SetForeground(bool isForeground);

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

    /// <summary>
    /// Raised when an HTML element inside the page enters or leaves fullscreen,
    /// such as a video using the Fullscreen API.
    /// </summary>
    /// <remarks>
    /// The engine only fills its own surface. The host has to hide window chrome
    /// in response, or the video sits under the title bar and toolbar. Only
    /// raised where the platform exposes the signal — WebView2 on Windows today.
    /// </remarks>
    event EventHandler<EngineFullScreenElementChangedEventArgs>? FullScreenElementChanged;

    /// <summary>
    /// Raised for keys WebView2 treats as accelerators (F11, Escape, Ctrl/Alt
    /// combos) while the page has focus. The host must set
    /// <see cref="EngineAcceleratorKeyPressedEventArgs.Handled"/> before returning
    /// if it wants the engine to ignore the key; the window then acts on it.
    /// </summary>
    event EventHandler<EngineAcceleratorKeyPressedEventArgs>? AcceleratorKeyPressed;

    /// <summary>
    /// Finds <paramref name="text"/> in the current document. Returns how many
    /// matches exist and which one is selected, or null when the engine cannot
    /// search this surface.
    /// </summary>
    Task<EngineFindResult?> FindAsync(string text, bool forward, bool matchCase);

    /// <summary>Clears the current find highlight, if any.</summary>
    Task StopFindAsync();

    /// <summary>Asks the page to print, using the engine's print UI when it has one.</summary>
    Task PrintAsync();

    /// <summary>
    /// Opens the engine's developer tools. Returns false on hosts that do not
    /// expose a tools window.
    /// </summary>
    bool OpenDevTools();

    /// <summary>Mutes or unmutes media elements in the current document.</summary>
    Task SetMutedAsync(bool muted);

    /// <summary>
    /// Starts fetching <paramref name="source"/> as a download the application
    /// can pause, resume and cancel. Used on hosts whose engine does not raise
    /// <see cref="DownloadStarted"/>, and for "Save link as" from the page menu.
    /// </summary>
    IEngineDownloadOperation StartDownload(Uri source, string filePath);

    /// <summary>
    /// Page-originated UI requests that the chrome must handle: a custom context
    /// menu, a Fullscreen API change on hosts without a native signal, or a
    /// download the page asked for itself.
    /// </summary>
    event EventHandler<EnginePageMessageEventArgs>? PageMessage;
}

public sealed class EngineFindResult(int matchCount, int activeIndex)
{
    public int MatchCount { get; } = matchCount;

    public int ActiveIndex { get; } = activeIndex;
}

public sealed class EnginePageMessageEventArgs(string kind, string? payload) : EventArgs
{
    /// <summary>One of <c>ctx</c>, <c>fs</c>, <c>download</c>.</summary>
    public string Kind { get; } = kind;

    public string? Payload { get; } = payload;
}

public sealed class EngineDownloadStartedEventArgs(IEngineDownloadOperation operation) : EventArgs
{
    public IEngineDownloadOperation Operation { get; } = operation;
}

public sealed class EngineZoomFactorChangedEventArgs(double factor) : EventArgs
{
    public double Factor { get; } = factor;
}

public sealed class EngineFullScreenElementChangedEventArgs(bool containsFullScreenElement) : EventArgs
{
    public bool ContainsFullScreenElement { get; } = containsFullScreenElement;
}

public sealed class EngineAcceleratorKeyPressedEventArgs(int virtualKey, bool isKeyDown) : EventArgs
{
    /// <summary>Win32 virtual-key code, for example 0x7A (F11) or 0x1B (Escape).</summary>
    public int VirtualKey { get; } = virtualKey;

    public bool IsKeyDown { get; } = isKeyDown;

    /// <summary>
    /// Set during the raise. The engine reads it afterwards and tells WebView2
    /// whether the page should still see the key.
    /// </summary>
    public bool Handled { get; set; }
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
