using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Application.Dtos;

/// <summary>A browser page offered to bookmark or persist inside a saved group.</summary>
/// <param name="Address">
/// Null for a New Tab, which has no address to record but still holds its place
/// in the group — see <see cref="Domain.Entities.BlankPage"/>.
/// </param>
public sealed record BookmarkPageCandidate(
    string Title,
    PageAddress? Address,
    PageAddress? FaviconAddress,
    bool HasResolvedTitle = true,
    bool HasResolvedFavicon = true);
