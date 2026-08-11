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

    /// <summary>
    /// How the loader identifies itself when fetching an icon.
    /// </summary>
    /// <remarks>
    /// Not optional. Wikipedia and Stack Overflow, among others, answer a request
    /// carrying no user agent with 403, and their icons simply never arrived — the
    /// tab fell back to its glyph and the cause looked like a decoding problem
    /// rather than a refused request. It is deliberately shaped like a browser's,
    /// because that is what this is, and an unfamiliar agent invites the same
    /// treatment as none at all.
    /// </remarks>
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Aphelion/1.0";

    private readonly ConcurrentDictionary<string, byte[]?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public FaviconLoader()
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept",
            "image/avif,image/webp,image/png,image/svg+xml,image/*;q=0.8,*/*;q=0.5");
    }

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
