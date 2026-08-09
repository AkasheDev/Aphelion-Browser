using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Storage;

/// <summary>
/// Resolves the per-user profile directory using the platform's own convention:
/// %APPDATA%\Aphelion on Windows, ~/.config/Aphelion on Linux, and
/// ~/Library/Application Support/Aphelion on macOS.
/// </summary>
public sealed class UserDataLocation : IUserDataLocation
{
    public UserDataLocation()
    {
        var baseDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);

        // GetFolderPath returns empty when the platform has no such folder rather
        // than throwing. Falling back to the working directory would scatter user
        // data wherever the app happened to launch, so fail instead.
        if (string.IsNullOrEmpty(baseDirectory))
        {
            throw new InvalidOperationException(
                "Could not determine the per-user application data directory.");
        }

        RootDirectory = Path.Combine(baseDirectory, "Aphelion");
    }

    public string RootDirectory { get; }

    public void EnsureCreated() => Directory.CreateDirectory(RootDirectory);
}
