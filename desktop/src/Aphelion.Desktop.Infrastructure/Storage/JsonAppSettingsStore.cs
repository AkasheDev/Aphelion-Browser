using System.Text.Json;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Enums;

namespace Aphelion.Desktop.Infrastructure.Storage;

public sealed class JsonAppSettingsStore(IUserDataLocation location) : IAppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly IUserDataLocation _location =
        location ?? throw new ArgumentNullException(nameof(location));

    private string FilePath => Path.Combine(_location.RootDirectory, "app-settings.json");

    public event EventHandler? Changed;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return AppSettings.Default;
            }

            var file = JsonSerializer.Deserialize<AppSettingsFile>(File.ReadAllText(FilePath), Options);
            return file?.ToSettings() ?? AppSettings.Default;
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            _location.EnsureCreated();
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(AppSettingsFile.From(settings), Options));
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A preference write failure must never block the browser.
        }
    }

    private sealed record AppSettingsFile(
        ThemePreference Theme,
        StartupMode Startup,
        List<string>? StartupPages,
        string? DownloadFolder,
        UpdateChannel UpdateChannel,
        bool HasCompletedOnboarding)
    {
        public static AppSettingsFile From(AppSettings settings) => new(
            settings.Theme,
            settings.Startup,
            settings.StartupPages.ToList(),
            settings.DownloadFolder,
            settings.UpdateChannel,
            settings.HasCompletedOnboarding);

        public AppSettings ToSettings() => new(
            Enum.IsDefined(Theme) ? Theme : ThemePreference.System,
            Enum.IsDefined(Startup) ? Startup : StartupMode.RestoreSession,
            StartupPages ?? [],
            string.IsNullOrWhiteSpace(DownloadFolder) ? null : DownloadFolder,
            Enum.IsDefined(UpdateChannel) ? UpdateChannel : UpdateChannel.Stable,
            HasCompletedOnboarding);
    }
}
