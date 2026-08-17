using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Storage;

/// <summary>In-memory visit log for a private window. Nothing is written to disk.</summary>
public sealed class MemoryHistoryStore : IHistoryStore
{
    public IReadOnlyList<HistoryVisit> Load() => [];

    public void Add(HistoryVisit visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
    }

    public void Delete(HistoryVisit visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
    }

    public void Clear()
    {
    }
}
