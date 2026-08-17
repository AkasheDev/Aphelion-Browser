namespace Aphelion.Desktop.Domain;

/// <summary>
/// Pages this application draws itself, rather than handing to the engine.
/// Typed in the address bar as <c>aphelion://…</c> URLs, the same way Chrome
/// uses <c>chrome://</c>. They are not <see cref="ValueObjects.PageAddress"/>
/// values: that type only admits http(s), by design.
/// </summary>
public enum InternalPageKind
{
    Downloads,
}

/// <summary>
/// Addresses and titles of <see cref="InternalPageKind"/> pages. Adding a new
/// internal surface (settings, history) is a new kind plus a host here, not a
/// new scheme.
/// </summary>
public static class InternalPages
{
    public const string Scheme = "aphelion";

    public const string DownloadsAddress = "aphelion://downloads";

    public const string DownloadsTitle = "Downloads";

    public static bool IsDownloads(string? address) =>
        TryMatch(address, out var kind) && kind == InternalPageKind.Downloads;

    /// <summary>
    /// True when <paramref name="input"/> is an <c>aphelion://</c> URL, known
    /// page or not. Unknown hosts must not be sent to a search engine.
    /// </summary>
    public static bool IsAphelionAddress(string? input) =>
        TryGetHost(input, out _);

    /// <summary>
    /// Resolves typed address-bar text to a page this application draws.
    /// Trailing slashes and casing are ignored so the URL behaves like a site.
    /// </summary>
    public static bool TryMatch(string? input, out InternalPageKind kind)
    {
        kind = default;

        if (!TryGetHost(input, out var host))
        {
            return false;
        }

        if (string.Equals(host, "downloads", StringComparison.OrdinalIgnoreCase))
        {
            kind = InternalPageKind.Downloads;
            return true;
        }

        return false;
    }

    private static bool TryGetHost(string? input, out string host)
    {
        host = string.Empty;

        var trimmed = input?.Trim();
        if (string.IsNullOrEmpty(trimmed) ||
            !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        host = uri.Host;
        return host.Length > 0;
    }
}
