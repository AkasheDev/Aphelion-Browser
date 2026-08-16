namespace Aphelion.Desktop.Domain.Enums;

/// <summary>
/// Where a download is in its life. <see cref="InProgress"/> and
/// <see cref="Paused"/> are the live states; the other three are terminal.
/// </summary>
public enum DownloadState
{
    InProgress,
    Paused,
    Completed,
    Canceled,
    Failed,
}
