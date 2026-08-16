namespace Aphelion.Desktop.Application.Ports;

/// <summary>
/// A live download the engine is carrying out, controllable from above the
/// engine boundary.
/// </summary>
/// <remarks>
/// The engine owns the transfer — connection, retries, writing the file — and
/// this handle only observes and steers it. It is only valid while the engine
/// surface that produced it is alive: closing the tab that started a download
/// takes the transfer with it, which is a platform WebView limitation noted in
/// ADR-0001, not a choice.
/// </remarks>
public interface IEngineDownloadOperation
{
    /// <summary>Where the file is being fetched from, or null if unreadable.</summary>
    Uri? Source { get; }

    /// <summary>Absolute path being written, including the file name.</summary>
    string? FilePath { get; }

    long BytesReceived { get; }

    /// <summary>Expected total in bytes, or null when the server did not say.</summary>
    long? TotalBytes { get; }

    /// <summary>Whether a paused or interrupted transfer can pick up again.</summary>
    bool CanResume { get; }

    bool Pause();

    bool ResumeDownload();

    bool Cancel();

    /// <summary>Raised as bytes arrive. Read the properties for the new values.</summary>
    event EventHandler? ProgressChanged;

    /// <summary>Raised when the transfer pauses, resumes, completes or breaks.</summary>
    event EventHandler<EngineDownloadStateChangedEventArgs>? StateChanged;
}

/// <summary>Portable download states, mapped from whatever the engine reports.</summary>
public enum EngineDownloadState
{
    InProgress,
    Paused,
    Completed,
    Canceled,
    Failed,
}

public sealed class EngineDownloadStateChangedEventArgs(
    EngineDownloadState state,
    string? failureReason = null) : EventArgs
{
    public EngineDownloadState State { get; } = state;

    /// <summary>Engine's reason text when <see cref="State"/> is Failed.</summary>
    public string? FailureReason { get; } = failureReason;
}
