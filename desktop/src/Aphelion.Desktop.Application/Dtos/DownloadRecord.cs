namespace Aphelion.Desktop.Application.Dtos;

/// <summary>
/// One download as persisted between runs. Strings rather than domain types so
/// the stored file survives refactors and a corrupt field spoils one entry, not
/// the whole history.
/// </summary>
public sealed record DownloadRecord(
    string Id,
    string Url,
    string FilePath,
    long? TotalBytes,
    long ReceivedBytes,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);
