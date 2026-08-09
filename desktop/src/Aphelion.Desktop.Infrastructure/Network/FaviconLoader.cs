using System.Collections.Concurrent;
using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Network;

/// <summary>
/// Fetches favicons over HTTP and keeps them in memory for the session.
/// </summary>
/// <remarks>
/// In-memory only: the strip re-requests the same handful of icons constantly,
/// and a disk cache is a privacy decision — a favicon store is a record of sites
/// visited, which belongs with history, not ahead of it.
/// </remarks>
public sealed class FaviconLoader : IFaviconLoader, IDisposable
{
    /// <summary>A favicon far larger than this is not a favicon.</summary>
    private const int MaxBytes = 512 * 1024;

    private readonly ConcurrentDictionary<string, byte[]?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public async Task<byte[]?> LoadAsync(Uri address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        var key = address.AbsoluteUri;

        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        byte[]? bytes = null;

        try
        {
            using var response = await _http
                .GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode &&
                response.Content.Headers.ContentLength is null or <= MaxBytes)
            {
                var payload = await response.Content
                    .ReadAsByteArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (payload.Length is > 0 and <= MaxBytes)
                {
                    bytes = payload;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            // A missing or unreachable favicon is ordinary; the tab shows its
            // fallback glyph instead.
        }

        // Failures are cached too, so a site without an icon is not re-fetched on
        // every navigation.
        _cache[key] = bytes;
        return bytes;
    }

    public void Dispose() => _http.Dispose();
}
