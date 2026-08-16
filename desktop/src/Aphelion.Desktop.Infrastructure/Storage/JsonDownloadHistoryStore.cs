using System.Text.Json;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Storage;

/// <summary>Small, fault-tolerant JSON store for the download history.</summary>
/// <remarks>
/// The whole list is written on every save, which is fine at this size: the
/// history is capped by its view model, and saves happen on state transitions
/// rather than on every progress tick. An unreadable file loads as an empty
/// history rather than failing — losing the list is bad, refusing to start the
/// browser over it would be worse.
/// </remarks>
public sealed class JsonDownloadHistoryStore(IUserDataLocation location) : IDownloadHistoryStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly IUserDataLocation _location =
        location ?? throw new ArgumentNullException(nameof(location));

    private string FilePath => Path.Combine(_location.RootDirectory, "downloads.json");

    public IReadOnlyList<DownloadRecord> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<DownloadRecord>>(File.ReadAllText(FilePath), Options) ?? [];
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<DownloadRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        try
        {
            _location.EnsureCreated();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(records, Options));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed history write must never interfere with the download
            // itself; the next state transition tries again.
        }
    }
}
