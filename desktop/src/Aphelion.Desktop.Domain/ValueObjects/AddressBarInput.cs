namespace Aphelion.Desktop.Domain.ValueObjects;

/// <summary>
/// What the user typed in the address bar, resolved to either a navigable address
/// or a search term.
/// </summary>
public enum AddressBarIntent
{
    /// <summary>Input is empty or whitespace; nothing to do.</summary>
    Empty,

    /// <summary>Input resolved to a navigable address.</summary>
    Navigate,

    /// <summary>Input is not an address and should be handed to a search engine.</summary>
    Search,
}

/// <summary>
/// Interprets raw address bar text. Deciding whether "example.com" is an address
/// and "how to cook rice" is a search is a product rule, so it lives in Domain
/// rather than in the view.
/// </summary>
public static class AddressBarInput
{
    public static AddressBarIntent Resolve(string? input, out PageAddress? address, out string searchTerm)
    {
        address = null;
        searchTerm = string.Empty;

        var trimmed = input?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return AddressBarIntent.Empty;
        }

        // An explicit scheme is taken at face value. If it is one we refuse to
        // navigate to, the input becomes a search rather than silently failing —
        // except aphelion://, which is this application's own pages. Known hosts
        // are opened by the shell; unknown ones are not a web search.
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var explicitUri))
        {
            if (PageAddress.TryCreate(explicitUri, out address))
            {
                return AddressBarIntent.Navigate;
            }

            if (string.Equals(explicitUri.Scheme, InternalPages.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                return AddressBarIntent.Empty;
            }

            searchTerm = trimmed;
            return AddressBarIntent.Search;
        }

        // No scheme. Treat it as a host only when it looks like one: a dot with
        // something either side, and no spaces. "example.com" navigates,
        // "how to cook rice" and "rice.and beans" search.
        if (LooksLikeHost(trimmed) &&
            Uri.TryCreate("https://" + trimmed, UriKind.Absolute, out var assumedUri) &&
            PageAddress.TryCreate(assumedUri, out address))
        {
            return AddressBarIntent.Navigate;
        }

        address = null;
        searchTerm = trimmed;
        return AddressBarIntent.Search;
    }

    private static bool LooksLikeHost(string value)
    {
        if (value.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        var host = value.Split('/', 2)[0];
        var dot = host.IndexOf('.', StringComparison.Ordinal);

        return dot > 0 && dot < host.Length - 1;
    }
}
