using System.Text;
using System.Text.Json;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Storage;

/// <summary>
/// Stores credentials in the profile directory. The file is written for this
/// user only; it is not encrypted, and it is never sent anywhere. Filling these
/// values into a page is a separate decision and is not done from this store.
/// </summary>
public sealed class JsonPasswordStore(IUserDataLocation location) : IPasswordStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly IUserDataLocation _location =
        location ?? throw new ArgumentNullException(nameof(location));

    private string FilePath => Path.Combine(_location.RootDirectory, "passwords.json");

    public IReadOnlyList<SavedCredential> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<SavedCredential>>(File.ReadAllText(FilePath), Options)
                ?? [];
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void SaveAll(IReadOnlyList<SavedCredential> credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        try
        {
            _location.EnsureCreated();
            var json = JsonSerializer.Serialize(credentials.ToList(), Options);
            File.WriteAllText(FilePath, json, Encoding.UTF8);
            RestrictToOwner(FilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A password write failure must never take the settings page down.
        }
    }

    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
            // Best effort: the profile directory is already per-user.
        }
    }
}
