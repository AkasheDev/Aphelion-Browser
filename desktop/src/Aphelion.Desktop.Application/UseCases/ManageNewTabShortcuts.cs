using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Application.UseCases;

/// <summary>Owns the ordered, user-editable launcher row on the New Tab page.</summary>
public sealed class ManageNewTabShortcuts
{
    public const int MaximumCount = 7;

    private readonly INewTabShortcutStore _store;
    private readonly List<NewTabShortcut> _items = [];

    public ManageNewTabShortcuts(INewTabShortcutStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));

        var saved = _store.Load();

        if (saved is null)
        {
            SeedDefaults();
            Persist();
        }
        else
        {
            Restore(saved);
        }
    }

    public IReadOnlyList<NewTabShortcut> Items => _items;

    public event EventHandler? Changed;

    public bool TryAdd(string name, PageAddress address, out string? error)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (_items.Count >= MaximumCount)
        {
            error = $"You can keep up to {MaximumCount} shortcuts.";
            return false;
        }

        if (_items.Any(item => item.Address.Equals(address)))
        {
            error = "That address is already in your shortcuts.";
            return false;
        }

        try
        {
            _items.Add(new NewTabShortcut(NewTabShortcutId.New(), name, address));
        }
        catch (ArgumentOutOfRangeException)
        {
            error = $"Names can contain at most {NewTabShortcut.MaximumNameLength} characters.";
            return false;
        }
        catch (ArgumentException)
        {
            error = "Enter a shortcut name.";
            return false;
        }

        Commit();
        error = null;
        return true;
    }

    public bool TryUpdate(NewTabShortcutId id, string name, PageAddress address, out string? error)
    {
        var shortcut = _items.Find(item => item.Id == id);

        if (shortcut is null)
        {
            error = "The shortcut no longer exists.";
            return false;
        }

        if (_items.Any(item => item.Id != id && item.Address.Equals(address)))
        {
            error = "That address is already in your shortcuts.";
            return false;
        }

        try
        {
            shortcut.Update(name, address);
        }
        catch (ArgumentOutOfRangeException)
        {
            error = $"Names can contain at most {NewTabShortcut.MaximumNameLength} characters.";
            return false;
        }
        catch (ArgumentException)
        {
            error = "Enter a shortcut name.";
            return false;
        }

        Commit();
        error = null;
        return true;
    }

    public void Remove(NewTabShortcutId id)
    {
        if (_items.RemoveAll(item => item.Id == id) > 0)
        {
            Commit();
        }
    }

    private void Restore(IReadOnlyList<NewTabShortcutSnapshot> saved)
    {
        foreach (var snapshot in saved.Take(MaximumCount))
        {
            if (snapshot.Id == Guid.Empty ||
                !PageAddress.TryCreate(
                    Uri.TryCreate(snapshot.Address, UriKind.Absolute, out var uri) ? uri : null,
                    out var address) ||
                address is null)
            {
                continue;
            }

            try
            {
                _items.Add(new NewTabShortcut(
                    new NewTabShortcutId(snapshot.Id),
                    snapshot.Name,
                    address));
            }
            catch (ArgumentException)
            {
                // One invalid entry must not discard the rest of the user's row.
            }
        }
    }

    private void SeedDefaults()
    {
        AddDefault("YouTube", "https://www.youtube.com/");
        AddDefault("GitHub", "https://github.com/");
        AddDefault("Wikipedia", "https://www.wikipedia.org/");
        AddDefault("Reddit", "https://www.reddit.com/");
        AddDefault("Mail", "https://mail.google.com/");
        AddDefault("Spotify", "https://open.spotify.com/");
    }

    private void AddDefault(string name, string value)
    {
        if (PageAddress.TryCreate(new Uri(value), out var address) && address is not null)
        {
            _items.Add(new NewTabShortcut(NewTabShortcutId.New(), name, address));
        }
    }

    private void Commit()
    {
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Persist() => _store.Save(
        _items
            .Select(item => new NewTabShortcutSnapshot(
                item.Id.Value,
                item.Name,
                item.Address.ToString()))
            .ToArray());
}
