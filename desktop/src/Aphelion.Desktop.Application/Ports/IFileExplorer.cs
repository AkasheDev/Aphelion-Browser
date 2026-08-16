namespace Aphelion.Desktop.Application.Ports;

/// <summary>
/// Hands files and folders to the operating system's own shell: opening a
/// downloaded file with its default application, or showing it in the file
/// manager.
/// </summary>
/// <remarks>
/// A port because each operating system has its own way of doing this, and
/// view models must not reach for the process API themselves. Every operation
/// is best effort: a missing file or a shell refusal returns false rather than
/// throwing, since none of these actions is worth interrupting browsing for.
/// </remarks>
public interface IFileExplorer
{
    /// <summary>The user's downloads folder, whether or not it exists yet.</summary>
    string DownloadsDirectory { get; }

    /// <summary>Opens a file with whatever the system associates with it.</summary>
    bool OpenFile(string path);

    /// <summary>Opens the file manager with the file selected, where supported.</summary>
    bool RevealInFolder(string path);

    /// <summary>Opens a folder in the file manager.</summary>
    bool OpenFolder(string path);
}
