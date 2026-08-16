namespace Aphelion.Desktop.Domain;

/// <summary>
/// Addresses and titles of pages this application draws itself, rather than
/// handing to the engine. They are not <c>PageAddress</c> values: that type
/// only admits http(s), by design.
/// </summary>
public static class InternalPages
{
    public const string DownloadsAddress = "aphelion://downloads";

    public const string DownloadsTitle = "Downloads";

    public static bool IsDownloads(string? address) =>
        string.Equals(address, DownloadsAddress, StringComparison.OrdinalIgnoreCase);
}
