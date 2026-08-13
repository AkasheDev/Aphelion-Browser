using System.Text.Json;
using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Storage;

public sealed class JsonPrivacyPreferenceStore(IUserDataLocation location) : IPrivacyPreferenceStore
{
    private readonly IUserDataLocation _location =
        location ?? throw new ArgumentNullException(nameof(location));

    private string FilePath => Path.Combine(_location.RootDirectory, "privacy-preferences.json");

    public PrivacyPreferences Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<PrivacyPreferences>(File.ReadAllText(FilePath))
                    ?? PrivacyPreferences.Default
                : PrivacyPreferences.Default;
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return PrivacyPreferences.Default;
        }
    }

    public void Save(PrivacyPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        try
        {
            _location.EnsureCreated();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(preferences));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A preference write failure must never block New Tab from working.
        }
    }
}
