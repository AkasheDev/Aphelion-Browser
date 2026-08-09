namespace Aphelion.Desktop.Application.Ports;

/// <summary>
/// One unit of work that must finish before the main window opens.
/// </summary>
/// <remarks>
/// The splash screen exists to cover this work. Today there is little of it, but
/// loading local user data — history, bookmarks, settings — will run here, so the
/// startup sequence is modelled properly from the start rather than being faked
/// with a delay.
/// </remarks>
public interface IStartupTask
{
    /// <summary>Shown on the splash screen while this task runs.</summary>
    string Description { get; }

    Task ExecuteAsync(CancellationToken cancellationToken);
}
