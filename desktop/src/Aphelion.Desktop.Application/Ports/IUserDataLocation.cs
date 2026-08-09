namespace Aphelion.Desktop.Application.Ports;

/// <summary>
/// Where this installation keeps the user's data on disk.
/// </summary>
/// <remarks>
/// A port rather than a direct path lookup: the correct directory differs per
/// operating system, and tests must be able to point it somewhere disposable.
/// </remarks>
public interface IUserDataLocation
{
    /// <summary>Root directory for profile data. Not guaranteed to exist yet.</summary>
    string RootDirectory { get; }

    /// <summary>Creates the directory tree if it is missing.</summary>
    void EnsureCreated();
}
