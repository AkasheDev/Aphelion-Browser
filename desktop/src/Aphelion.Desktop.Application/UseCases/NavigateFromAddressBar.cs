using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Application.UseCases;

/// <summary>
/// Takes what the user typed in the address bar and drives the tab and engine
/// accordingly: navigate to it, or search for it.
/// </summary>
public sealed class NavigateFromAddressBar(ISearchQueryBuilder searchQueryBuilder)
{
    private readonly ISearchQueryBuilder _searchQueryBuilder =
        searchQueryBuilder ?? throw new ArgumentNullException(nameof(searchQueryBuilder));

    /// <summary>
    /// Resolves <paramref name="input"/> and starts navigation. Returns false when
    /// the input was empty and nothing was done.
    /// </summary>
    public bool Execute(BrowserTab tab, IBrowserEngineSession session, string? input)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(session);

        var intent = AddressBarInput.Resolve(input, out var address, out var searchTerm);

        var target = intent switch
        {
            AddressBarIntent.Navigate => address,
            AddressBarIntent.Search => _searchQueryBuilder.BuildSearchAddress(searchTerm),
            _ => null,
        };

        if (target is null)
        {
            return false;
        }

        tab.BeginNavigation(target);
        session.Navigate(target);
        return true;
    }
}
