using System.Text.Json;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Storage;

public sealed class JsonHistoryStore(IUserDataLocation location) : IHistoryStore
{
    private const int Limit = 2000;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly IUserDataLocation _location =
        location ?? throw new ArgumentNullException(nameof(location));

    private readonly object _gate = new();

    private string FilePath => Path.Combine(_location.RootDirectory, "history.json");

    public IReadOnlyList<HistoryVisit> Load()
    {
        lock (_gate)
        {
            return Read();
        }
    }

    public void Add(HistoryVisit visit)
    {
        ArgumentNullException.ThrowIfNull(visit);

        lock (_gate)
        {
            var visits = Read().ToList();
            visits.Insert(0, visit);

            if (visits.Count > Limit)
            {
                visits.RemoveRange(Limit, visits.Count - Limit);
            }

            Write(visits);
        }
    }

    public void Delete(HistoryVisit visit)
    {
        ArgumentNullException.ThrowIfNull(visit);

        lock (_gate)
        {
            var visits = Read()
                .Where(item =>
                    item.VisitedAt != visit.VisitedAt ||
                    !string.Equals(item.Address, visit.Address, StringComparison.Ordinal))
                .ToList();
            Write(visits);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Write([]);
        }
    }

    private List<HistoryVisit> Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<HistoryVisit>>(File.ReadAllText(FilePath), Options)
                ?? [];
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Write(List<HistoryVisit> visits)
    {
        try
        {
            _location.EnsureCreated();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(visits, Options));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A history write failure must never block navigation.
        }
    }
}
