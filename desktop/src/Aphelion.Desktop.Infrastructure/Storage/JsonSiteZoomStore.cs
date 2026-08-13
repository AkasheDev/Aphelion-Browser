using System.Text.Json;
using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Storage;

/// <summary>Small, fault-tolerant JSON store for site zoom preferences.</summary>
/// <remarks>
/// Every value is kept in memory once read, because zoom is set from repeatable
/// button presses (in, out, reset) rather than a single commit — reading the
/// whole file back on every one of those, only to change one entry, would turn a
/// few quick clicks into a few quick round trips to disk. The in-memory copy is
/// this instance's own; it is a singleton for the process, so nothing else
/// writes the file underneath it.
/// </remarks>
public sealed class JsonSiteZoomStore(IUserDataLocation location) : ISiteZoomStore
{
    private readonly IUserDataLocation _location = location ?? throw new ArgumentNullException(nameof(location));
    private Dictionary<string, int>? _cache;

    private string FilePath => Path.Combine(_location.RootDirectory, "site-zoom.json");

    public int? Load(string origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        return ReadCached().TryGetValue(origin, out var percent) ? percent : null;
    }

    public void Save(string origin, int percent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        try
        {
            var values = ReadCached();
            values[origin] = percent;
            _location.EnsureCreated();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(values));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed preference write must never interfere with browsing. The
            // cache still holds the new value, so this session behaves as if it
            // had been saved even though the next launch will not see it.
        }
    }

    private Dictionary<string, int> ReadCached()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        try
        {
            _cache = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(FilePath))
                    ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _cache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        return _cache;
    }
}
