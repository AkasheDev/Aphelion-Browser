using Aphelion.Desktop.Application.Dtos;

namespace Aphelion.Desktop.Application.Ports;

/// <summary>Persists New Tab launchers separately from bookmarks and session state.</summary>
public interface INewTabShortcutStore
{
    /// <summary>Null means no saved preference; an empty list means the user removed all launchers.</summary>
    IReadOnlyList<NewTabShortcutSnapshot>? Load();

    void Save(IReadOnlyList<NewTabShortcutSnapshot> shortcuts);
}
