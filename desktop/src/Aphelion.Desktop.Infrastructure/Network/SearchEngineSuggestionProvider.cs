using System.Text.Json;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Enums;

namespace Aphelion.Desktop.Infrastructure.Network;

/// <summary>Adapts each supported search provider's lightweight suggestion feed.</summary>
public sealed class SearchEngineSuggestionProvider : ISearchSuggestionProvider, IDisposable
{
    private const int MaximumSuggestions = 7;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public SearchEngineSuggestionProvider()
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 Aphelion/1.0");
    }

    public async Task<IReadOnlyList<string>> GetSuggestionsAsync(
        SearchEngineKind engine,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        try
        {
            var url = BuildUrl(engine, query.Trim());
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return Extract(engine, document.RootElement);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or JsonException
                or InvalidOperationException
                or KeyNotFoundException)
        {
            return [];
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string BuildUrl(SearchEngineKind engine, string query)
    {
        var escaped = Uri.EscapeDataString(query);

        return engine switch
        {
            SearchEngineKind.Google =>
                $"https://suggestqueries.google.com/complete/search?client=firefox&q={escaped}",
            SearchEngineKind.Yandex =>
                $"https://suggest.yandex.com/suggest-ff.cgi?part={escaped}&uil=tr",
            SearchEngineKind.Bing =>
                $"https://api.bing.com/osjson.aspx?query={escaped}",
            SearchEngineKind.Brave =>
                $"https://search.brave.com/api/suggest?q={escaped}",
            SearchEngineKind.Yahoo =>
                $"https://search.yahoo.com/sugg/gossip/gossip-us-ura/?output=sd1&command={escaped}",
            _ => $"https://duckduckgo.com/ac/?q={escaped}",
        };
    }

    private static string[] Extract(SearchEngineKind engine, JsonElement root)
    {
        var values = engine switch
        {
            SearchEngineKind.DuckDuckGo => root
                .EnumerateArray()
                .Where(item => item.TryGetProperty("phrase", out _))
                .Select(item => item.GetProperty("phrase").GetString()),
            SearchEngineKind.Yahoo => root
                .GetProperty("r")
                .EnumerateArray()
                .Where(item => item.TryGetProperty("k", out _))
                .Select(item => item.GetProperty("k").GetString()),
            _ => root[1].EnumerateArray().Select(item => item.GetString()),
        };

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumSuggestions)
            .ToArray();
    }

}
