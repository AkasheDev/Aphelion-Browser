using System.Text.Json;
using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Storage;

/// <summary>Small, fault-tolerant JSON store for site zoom preferences.</summary>
public sealed class JsonSiteZoomStore(IUserDataLocation location) : ISiteZoomStore
{
    private readonly IUserDataLocation _location = location ?? throw new ArgumentNullException(nameof(location));

    private string FilePath => Path.Combine(_location.RootDirectory, "site-zoom.json");

    public int? Load(string origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        try
        {
            return Read().TryGetValue(origin, out var percent) ? percent : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string origin, int percent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        try
        {
            _location.EnsureCreated();
            var values = Read();
            values[origin] = percent;
            File.WriteAllText(FilePath, JsonSerializer.Serialize(values));
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // A failed preference write must never interfere with browsing.
        }
    }

    private Dictionary<string, int> Read()
    {
        if (!File.Exists(FilePath))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(FilePath))
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }
}
