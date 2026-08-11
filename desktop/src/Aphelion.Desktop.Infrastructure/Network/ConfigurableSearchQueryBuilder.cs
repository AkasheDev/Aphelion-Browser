using System.Globalization;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Enums;
using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Infrastructure.Network;

/// <summary>Builds searches with, and persists, the process-wide selected engine.</summary>
public sealed class ConfigurableSearchQueryBuilder : ISearchQueryBuilder, ISearchEnginePreference
{
    private readonly ISearchEnginePreferenceStore _store;

    public ConfigurableSearchQueryBuilder(ISearchEnginePreferenceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Selected = store.Load() ?? SearchEngineKind.DuckDuckGo;
    }

    public SearchEngineKind Selected { get; private set; }

    public event EventHandler? Changed;

    public PageAddress BuildSearchAddress(string searchTerm)
    {
        var query = Uri.EscapeDataString(searchTerm ?? string.Empty);
        var value = Selected switch
        {
            SearchEngineKind.Google => $"https://www.google.com/search?q={query}",
            SearchEngineKind.Yandex => $"https://yandex.com/search/?text={query}",
            SearchEngineKind.Bing => $"https://www.bing.com/search?q={query}",
            SearchEngineKind.Brave => $"https://search.brave.com/search?q={query}",
            SearchEngineKind.Yahoo => $"https://search.yahoo.com/search?p={query}",
            _ => $"https://duckduckgo.com/?q={query}",
        };
        var url = string.Create(CultureInfo.InvariantCulture, $"{value}");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !PageAddress.TryCreate(uri, out var address)
            || address is null)
        {
            throw new InvalidOperationException("Could not build the search address.");
        }

        return address;
    }

    public void ChangeTo(SearchEngineKind engine)
    {
        if (!Enum.IsDefined(engine) || Selected == engine)
        {
            return;
        }

        Selected = engine;
        _store.Save(engine);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
