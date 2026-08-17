using Aphelion.Desktop.Application.Dtos;

namespace Aphelion.Desktop.Application.Ports;

/// <summary>
/// The visit log. Distinct from the session snapshot: this is where the user
/// has been, not what is open now.
/// </summary>
public interface IHistoryStore
{
    IReadOnlyList<HistoryVisit> Load();

    void Add(HistoryVisit visit);

    void Delete(HistoryVisit visit);

    void Clear();
}
