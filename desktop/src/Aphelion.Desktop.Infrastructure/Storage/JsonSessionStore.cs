using System.Text.Json;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Storage;

/// <summary>
/// Stores the session as JSON in the profile directory.
/// </summary>
public sealed class JsonSessionStore(IUserDataLocation location) : ISessionStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly IUserDataLocation _location =
        location ?? throw new ArgumentNullException(nameof(location));

    private string FilePath => Path.Combine(_location.RootDirectory, "session.json");

    public SessionSnapshot? Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<SessionSnapshot>(File.ReadAllText(FilePath), Options)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable session file must not stop the browser from
            // starting; the cost is starting with a fresh tab.
            return null;
        }
    }

    public void Save(SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            _location.EnsureCreated();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Failing to persist must not block shutdown; the session is simply
            // not restored next time.
        }
    }
}
