using System.Text.Json;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Storage;

/// <summary>
/// Stores the bookmark hierarchy as JSON in the profile directory.
/// </summary>
public sealed class JsonBookmarkStore(IUserDataLocation location) : IBookmarkStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly IUserDataLocation _location =
        location ?? throw new ArgumentNullException(nameof(location));

    private string FilePath => Path.Combine(_location.RootDirectory, "bookmarks.json");

    public BookmarkSnapshot? Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<BookmarkSnapshot>(File.ReadAllText(FilePath), Options)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // An unreadable bookmarks file must not stop the browser from
            // starting; the cost is starting with an empty bar.
            return null;
        }
    }

    public void Save(BookmarkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            _location.EnsureCreated();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Failing to persist must not break the edit the user just made; it
            // is simply not there next time.
        }
    }
}
