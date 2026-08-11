using System.Text.Json;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Storage;

/// <summary>Stores New Tab launchers in their own profile file.</summary>
public sealed class JsonNewTabShortcutStore(IUserDataLocation location) : INewTabShortcutStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly IUserDataLocation _location =
        location ?? throw new ArgumentNullException(nameof(location));

    private string FilePath => Path.Combine(_location.RootDirectory, "new-tab-shortcuts.json");

    public IReadOnlyList<NewTabShortcutSnapshot>? Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<NewTabShortcutSnapshot[]>(File.ReadAllText(FilePath), Options)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(IReadOnlyList<NewTabShortcutSnapshot> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);

        try
        {
            _location.EnsureCreated();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(shortcuts, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a layout preference must never interrupt browsing.
        }
    }
}
