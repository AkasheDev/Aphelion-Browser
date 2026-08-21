using System.Text.Json;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Storage;

/// <summary>
/// The visit log on disk, held in memory while the application runs.
/// </summary>
/// <remarks>
/// <para>
/// Recording a visit used to read the whole file, parse it, insert, serialise
/// and write it back, and the page then read and parsed the file a second time
/// to redraw - three full passes over up to <see cref="Limit"/> records on the
/// UI thread, on every navigation, growing as the log filled.
/// </para>
/// <para>
/// The list is now read once and kept. Mutations copy it, swap the reference and
/// schedule a write, so a reader that is already walking the previous snapshot is
/// never mutated underneath and a navigation never waits on the disk. Writes
/// coalesce over <see cref="WriteDelay"/>: a burst of visits costs one write, and
/// <see cref="Dispose"/> flushes whatever is still pending at shutdown.
/// </para>
/// </remarks>
public sealed class JsonHistoryStore(IUserDataLocation location) : IHistoryStore, IDisposable
{
    private const int Limit = 2000;

    /// <summary>How long a write waits for further changes before it goes out.</summary>
    private static readonly TimeSpan WriteDelay = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly IUserDataLocation _location =
        location ?? throw new ArgumentNullException(nameof(location));

    private readonly object _gate = new();

    private IReadOnlyList<HistoryVisit>? _snapshot;
    private Timer? _flush;
    private bool _pending;
    private bool _disposed;

    private string FilePath => Path.Combine(_location.RootDirectory, "history.json");

    public IReadOnlyList<HistoryVisit> Load()
    {
        lock (_gate)
        {
            return Current();
        }
    }

    public void Add(HistoryVisit visit)
    {
        ArgumentNullException.ThrowIfNull(visit);

        lock (_gate)
        {
            var current = Current();
            var capacity = Math.Min(current.Count + 1, Limit);
            var next = new List<HistoryVisit>(capacity) { visit };

            for (var i = 0; i < current.Count && next.Count < Limit; i++)
            {
                next.Add(current[i]);
            }

            Replace(next);
        }
    }

    public void Delete(HistoryVisit visit)
    {
        ArgumentNullException.ThrowIfNull(visit);

        lock (_gate)
        {
            Replace(Current()
                .Where(item =>
                    item.VisitedAt != visit.VisitedAt ||
                    !string.Equals(item.Address, visit.Address, StringComparison.Ordinal))
                .ToList());
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Replace([]);
        }
    }

    /// <summary>Writes anything still pending. Called on shutdown by the container.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _flush?.Dispose();
            _flush = null;

            if (_pending)
            {
                _pending = false;
                Write(_snapshot ?? []);
            }
        }
    }

    /// <summary>The current list, read from disk the first time it is asked for.</summary>
    private IReadOnlyList<HistoryVisit> Current() => _snapshot ??= Read();

    /// <summary>
    /// Swaps in a new list and schedules the write. The old one is left alone so
    /// a reader holding it keeps a consistent view.
    /// </summary>
    private void Replace(List<HistoryVisit> next)
    {
        _snapshot = next;

        if (_disposed)
        {
            return;
        }

        _pending = true;
        _flush ??= new Timer(_ => FlushIfPending(), null, Timeout.Infinite, Timeout.Infinite);
        _flush.Change(WriteDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Runs on a pool thread, so the disk is never on the navigation path.</summary>
    private void FlushIfPending()
    {
        lock (_gate)
        {
            if (!_pending || _disposed)
            {
                return;
            }

            _pending = false;
            Write(_snapshot ?? []);
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

    private void Write(IReadOnlyList<HistoryVisit> visits)
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
