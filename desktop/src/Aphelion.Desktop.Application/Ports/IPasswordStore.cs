using Aphelion.Desktop.Application.Dtos;

namespace Aphelion.Desktop.Application.Ports;

/// <summary>Device-local saved passwords. Not synced, not injected into pages.</summary>
public interface IPasswordStore
{
    IReadOnlyList<SavedCredential> Load();

    void SaveAll(IReadOnlyList<SavedCredential> credentials);
}
