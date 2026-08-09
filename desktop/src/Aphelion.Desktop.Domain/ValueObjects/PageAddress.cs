namespace Aphelion.Desktop.Domain.ValueObjects;

/// <summary>
/// A validated address the browser is allowed to navigate to.
/// </summary>
/// <remarks>
/// Only http and https are accepted. Schemes such as file, javascript and data are
/// rejected deliberately: they are the usual vehicles for address-bar based attacks,
/// and nothing in the product requires them today. Widening this set is a security
/// decision, not a convenience one.
/// </remarks>
public sealed class PageAddress : IEquatable<PageAddress>
{
    private PageAddress(Uri value) => Value = value;

    public Uri Value { get; }

    public bool IsSecure => Value.Scheme == Uri.UriSchemeHttps;

    /// <summary>Host without a leading "www.", suitable for display.</summary>
    public string DisplayHost =>
        Value.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? Value.Host[4..]
            : Value.Host;

    public static bool TryCreate(Uri? value, out PageAddress? address)
    {
        address = null;

        if (value is null || !value.IsAbsoluteUri)
        {
            return false;
        }

        if (value.Scheme != Uri.UriSchemeHttp && value.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        address = new PageAddress(value);
        return true;
    }

    public override string ToString() => Value.AbsoluteUri;

    public bool Equals(PageAddress? other) => other is not null && Value == other.Value;

    public override bool Equals(object? obj) => Equals(obj as PageAddress);

    public override int GetHashCode() => Value.GetHashCode();
}
