using Aphelion.Desktop.Application.Dtos;

namespace Aphelion.Desktop.Application.Ports;

/// <summary>
/// Persists the bookmark hierarchy between runs.
/// </summary>
public interface IBookmarkStore
{
    /// <summary>The saved bookmarks, or null when there are none or they are unreadable.</summary>
    BookmarkSnapshot? Load();

    void Save(BookmarkSnapshot snapshot);
}
