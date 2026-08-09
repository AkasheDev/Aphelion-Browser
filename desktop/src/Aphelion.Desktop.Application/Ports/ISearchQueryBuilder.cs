using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Application.Ports;

/// <summary>
/// Turns a search term into the address of a results page. Which engine is used is
/// a user setting, so the application layer asks for the address rather than
/// building one itself.
/// </summary>
public interface ISearchQueryBuilder
{
    PageAddress BuildSearchAddress(string searchTerm);
}
