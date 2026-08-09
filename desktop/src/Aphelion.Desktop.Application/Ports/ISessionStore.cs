using Aphelion.Desktop.Application.Dtos;

namespace Aphelion.Desktop.Application.Ports;

/// <summary>
/// Persists the open-tab session between runs.
/// </summary>
public interface ISessionStore
{
    /// <summary>The saved session, or null when there is none or it is unreadable.</summary>
    SessionSnapshot? Load();

    void Save(SessionSnapshot snapshot);
}
