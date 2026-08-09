namespace Aphelion.Desktop.Application.Ports;

/// <summary>
/// Fetches favicon images for the tab strip.
/// </summary>
public interface IFaviconLoader
{
    /// <summary>
    /// The icon's bytes, or null when it could not be fetched. Implementations
    /// are expected to cache: the strip asks for the same icons repeatedly.
    /// </summary>
    Task<byte[]?> LoadAsync(Uri address, CancellationToken cancellationToken = default);
}
