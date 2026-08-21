using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Infrastructure.Storage;
using Xunit;

namespace Aphelion.Desktop.Tests;

/// <summary>
/// The visit log keeps itself in memory and writes on a delay, so what the
/// application reads back and what eventually lands on disk are two separate
/// questions. Both are pinned here.
/// </summary>
public sealed class JsonHistoryStoreTests : IDisposable
{
    private readonly TempLocation _location = new();

    public void Dispose() => _location.Dispose();

    [Fact]
    public void A_recorded_visit_is_readable_before_anything_is_written()
    {
        using var store = new JsonHistoryStore(_location);

        store.Add(Visit("https://example.com", "Example"));

        Assert.Equal("https://example.com", Assert.Single(store.Load()).Address);
    }

    [Fact]
    public void Newest_is_first()
    {
        using var store = new JsonHistoryStore(_location);

        store.Add(Visit("https://first.example", "First"));
        store.Add(Visit("https://second.example", "Second"));

        Assert.Equal(
            ["https://second.example", "https://first.example"],
            store.Load().Select(visit => visit.Address));
    }

    /// <summary>
    /// The page walks the list it was handed while navigation keeps recording.
    /// Mutating in place would break that walk, so a record has to leave the
    /// previous list alone.
    /// </summary>
    [Fact]
    public void A_list_already_handed_out_is_not_changed_by_a_later_record()
    {
        using var store = new JsonHistoryStore(_location);
        store.Add(Visit("https://first.example", "First"));

        var taken = store.Load();
        store.Add(Visit("https://second.example", "Second"));

        Assert.Single(taken);
        Assert.Equal(2, store.Load().Count);
    }

    [Fact]
    public void Disposing_flushes_what_the_delay_still_owes()
    {
        using (var store = new JsonHistoryStore(_location))
        {
            store.Add(Visit("https://example.com", "Example"));
        }

        using var reopened = new JsonHistoryStore(_location);
        Assert.Equal("https://example.com", Assert.Single(reopened.Load()).Address);
    }

    [Fact]
    public void Deleting_and_clearing_survive_a_reopen()
    {
        using (var store = new JsonHistoryStore(_location))
        {
            var doomed = Visit("https://gone.example", "Gone");
            store.Add(doomed);
            store.Add(Visit("https://kept.example", "Kept"));
            store.Delete(doomed);
        }

        using (var reopened = new JsonHistoryStore(_location))
        {
            Assert.Equal("https://kept.example", Assert.Single(reopened.Load()).Address);
            reopened.Clear();
        }

        using var emptied = new JsonHistoryStore(_location);
        Assert.Empty(emptied.Load());
    }

    [Fact]
    public void The_log_stops_growing_at_its_cap()
    {
        using var store = new JsonHistoryStore(_location);

        for (var i = 0; i < 2050; i++)
        {
            store.Add(Visit($"https://example.com/{i}", $"Page {i}"));
        }

        var visits = store.Load();
        Assert.Equal(2000, visits.Count);
        Assert.Equal("https://example.com/2049", visits[0].Address);
    }

    private static HistoryVisit Visit(string address, string title) =>
        new(DateTimeOffset.Now, address, title, null);

    private sealed class TempLocation : IUserDataLocation, IDisposable
    {
        public string RootDirectory { get; } =
            Path.Combine(Path.GetTempPath(), "aphelion-tests", Guid.NewGuid().ToString("N"));

        public void EnsureCreated() => Directory.CreateDirectory(RootDirectory);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootDirectory))
                {
                    Directory.Delete(RootDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test run over.
            }
        }
    }
}
