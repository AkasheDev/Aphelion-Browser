using Aphelion.Desktop.Application.Dtos;

namespace Aphelion.Desktop.Application.Ports;

/// <summary>
/// Persists the download history between runs.
/// </summary>
public interface IDownloadHistoryStore
{
    /// <summary>The saved history, newest first. Empty when there is none.</summary>
    IReadOnlyList<DownloadRecord> Load();

    void Save(IReadOnlyList<DownloadRecord> records);
}
