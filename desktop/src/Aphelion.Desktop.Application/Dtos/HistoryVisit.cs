namespace Aphelion.Desktop.Application.Dtos;

/// <summary>One recorded visit, independent of the open-tab session.</summary>
public sealed record HistoryVisit(
    DateTimeOffset VisitedAt,
    string Address,
    string Title,
    string? SearchTerm);
