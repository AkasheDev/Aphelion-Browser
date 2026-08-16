using Aphelion.Desktop.Domain.Enums;
using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Domain.Entities;

/// <summary>
/// One file the user is downloading or has downloaded.
/// </summary>
/// <remarks>
/// Owns the state rules: progress can only be reported while the download is
/// live, pausing and resuming only move between the two live states, and a
/// terminal state — completed, canceled, failed — is never left. The engine
/// merely reports what happened; this entity decides whether the report is a
/// legal transition, so a late event from a disposed engine surface cannot
/// resurrect a finished download.
/// </remarks>
public sealed class DownloadItem
{
    private DownloadItem(
        DownloadId id,
        Uri source,
        string filePath,
        long? totalBytes,
        long receivedBytes,
        DownloadState state,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt)
    {
        Id = id;
        Source = source;
        FilePath = filePath;
        TotalBytes = totalBytes;
        ReceivedBytes = receivedBytes;
        State = state;
        StartedAt = startedAt;
        EndedAt = endedAt;
    }

    /// <summary>Starts tracking a download the engine has just begun.</summary>
    public static DownloadItem Start(Uri source, string filePath, long? totalBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return new DownloadItem(
            DownloadId.New(),
            source,
            filePath,
            Normalize(totalBytes),
            receivedBytes: 0,
            DownloadState.InProgress,
            DateTimeOffset.Now,
            endedAt: null);
    }

    /// <summary>
    /// Rebuilds a download from its persisted record. A record claiming to be
    /// live is a download the previous run never finished — the process that
    /// carried it is gone, so it comes back failed rather than pretending to
    /// still be running.
    /// </summary>
    public static DownloadItem Restore(
        DownloadId id,
        Uri source,
        string filePath,
        long? totalBytes,
        long receivedBytes,
        DownloadState state,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var restoredState = state is DownloadState.InProgress or DownloadState.Paused
            ? DownloadState.Failed
            : state;

        return new DownloadItem(
            id,
            source,
            filePath,
            Normalize(totalBytes),
            Math.Max(0, receivedBytes),
            restoredState,
            startedAt,
            endedAt);
    }

    public DownloadId Id { get; }

    /// <summary>Where the file came from. A raw Uri rather than a PageAddress:
    /// downloads can be served from schemes a tab would never navigate to.</summary>
    public Uri Source { get; }

    /// <summary>Absolute path of the file on disk, including its name.</summary>
    public string FilePath { get; }

    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Expected size in bytes, or null when the server did not say.</summary>
    public long? TotalBytes { get; private set; }

    public long ReceivedBytes { get; private set; }

    public DownloadState State { get; private set; }

    public DateTimeOffset StartedAt { get; }

    /// <summary>When the download reached a terminal state, or null while live.</summary>
    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>Why the download failed, or null in any other state.</summary>
    public string? FailureReason { get; private set; }

    public bool IsLive => State is DownloadState.InProgress or DownloadState.Paused;

    /// <summary>Progress from 0 to 1, or null while the total is unknown.</summary>
    public double? ProgressFraction =>
        State == DownloadState.Completed
            ? 1d
            : TotalBytes is > 0
                ? Math.Clamp((double)ReceivedBytes / TotalBytes.Value, 0d, 1d)
                : null;

    /// <summary>Records bytes arriving. Ignored once the download has ended.</summary>
    public void ReportProgress(long receivedBytes, long? totalBytes)
    {
        if (!IsLive)
        {
            return;
        }

        ReceivedBytes = Math.Max(0, receivedBytes);
        TotalBytes = Normalize(totalBytes) ?? TotalBytes;
    }

    public bool Pause()
    {
        if (State != DownloadState.InProgress)
        {
            return false;
        }

        State = DownloadState.Paused;
        return true;
    }

    public bool Resume()
    {
        if (State != DownloadState.Paused)
        {
            return false;
        }

        State = DownloadState.InProgress;
        return true;
    }

    public bool Complete()
    {
        if (!IsLive)
        {
            return false;
        }

        // The whole file is on disk; a stale byte count must not read as 99%.
        if (TotalBytes is > 0)
        {
            ReceivedBytes = TotalBytes.Value;
        }

        State = DownloadState.Completed;
        EndedAt = DateTimeOffset.Now;
        return true;
    }

    public bool Cancel()
    {
        if (!IsLive)
        {
            return false;
        }

        State = DownloadState.Canceled;
        EndedAt = DateTimeOffset.Now;
        return true;
    }

    public bool Fail(string? reason)
    {
        if (!IsLive)
        {
            return false;
        }

        State = DownloadState.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? null : reason;
        EndedAt = DateTimeOffset.Now;
        return true;
    }

    /// <summary>The engine reports -1 for an unknown size; store that as null.</summary>
    private static long? Normalize(long? totalBytes) =>
        totalBytes is > 0 ? totalBytes : null;
}
