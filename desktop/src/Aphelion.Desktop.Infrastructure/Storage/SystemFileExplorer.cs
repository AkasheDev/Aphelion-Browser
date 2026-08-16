using System.Diagnostics;
using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Storage;

/// <summary>
/// Opens files and folders through the operating system's own shell.
/// </summary>
/// <remarks>
/// Selection-on-reveal is per platform: Windows Explorer and macOS Finder can
/// open with the file highlighted; Linux has no portable way to ask, so the
/// containing folder opens instead. Every operation returns false on failure —
/// see the port's remarks for why nothing here throws.
/// </remarks>
public sealed class SystemFileExplorer : IFileExplorer
{
    /// <summary>
    /// The user's downloads folder. The profile-relative convention — a
    /// "Downloads" directory under the user profile — holds on all three
    /// platforms, and is where the Windows engine writes by default.
    /// </summary>
    public string DownloadsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads");

    public bool OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        return TryStart(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public bool RevealInFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            // Explorer parses its own argument string; the path is quoted so
            // spaces survive, and /select highlights the file.
            return TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));
        }

        if (OperatingSystem.IsMacOS())
        {
            return TryStart(new ProcessStartInfo("open", $"-R \"{path}\""));
        }

        return OpenFolder(Path.GetDirectoryName(path) ?? path);
    }

    public bool OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        return TryStart(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static bool TryStart(ProcessStartInfo startInfo)
    {
        try
        {
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
