using Aphelion.Desktop.Domain.Enums;

namespace Aphelion.Desktop.Application.Dtos;

/// <summary>
/// Profile-wide choices that outlive a session: appearance, startup, downloads
/// and the first-run flag. Search-engine and New Tab privacy toggles stay in
/// their own stores so existing files keep working.
/// </summary>
public sealed record AppSettings(
    ThemePreference Theme,
    StartupMode Startup,
    IReadOnlyList<string> StartupPages,
    string? DownloadFolder,
    UpdateChannel UpdateChannel,
    bool HasCompletedOnboarding)
{
    public static AppSettings Default { get; } = new(
        ThemePreference.System,
        StartupMode.RestoreSession,
        [],
        DownloadFolder: null,
        UpdateChannel.Stable,
        HasCompletedOnboarding: false);
}
