using System.Globalization;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Infrastructure.Network;

/// <summary>
/// Builds DuckDuckGo result addresses.
/// </summary>
/// <remarks>
/// DuckDuckGo is the default because it does not profile users, which matches the
/// product's privacy stance. This is a stand-in for a configurable setting: once
/// settings exist, the engine becomes a user choice and this class becomes one of
/// several providers.
/// </remarks>
public sealed class DuckDuckGoSearchQueryBuilder : ISearchQueryBuilder
{
    public PageAddress BuildSearchAddress(string searchTerm)
    {
        var query = Uri.EscapeDataString(searchTerm ?? string.Empty);
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"https://duckduckgo.com/?q={query}");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !PageAddress.TryCreate(uri, out var address) ||
            address is null)
        {
            // Unreachable with a constant, escaped host: escaping cannot produce an
            // invalid absolute https URL. Fail loudly rather than return something
            // the caller would have to null-check.
            throw new InvalidOperationException($"Could not build a search address for '{searchTerm}'.");
        }

        return address;
    }
}
