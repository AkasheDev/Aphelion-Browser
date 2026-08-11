using Aphelion.Desktop.Domain.Enums;

namespace Aphelion.Desktop.Application.Ports;

/// <summary>Gets type-ahead suggestions from the currently selected provider.</summary>
public interface ISearchSuggestionProvider
{
    Task<IReadOnlyList<string>> GetSuggestionsAsync(
        SearchEngineKind engine,
        string query,
        CancellationToken cancellationToken = default);
}
