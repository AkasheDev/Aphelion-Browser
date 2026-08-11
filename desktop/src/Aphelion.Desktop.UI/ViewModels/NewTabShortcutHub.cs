using System.Collections.ObjectModel;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Application.UseCases;
using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// Process-wide projection of the application shortcut catalog. All open blank
/// tabs see one ordered row without every transient browser subscribing to the
/// catalog independently.
/// </summary>
public sealed class NewTabShortcutHub
{
    private readonly ManageNewTabShortcuts _manager;
    private readonly IFaviconLoader _favicons;
    private readonly Dictionary<NewTabShortcutId, NewTabShortcutViewModel> _cache = [];

    public NewTabShortcutHub(ManageNewTabShortcuts manager, IFaviconLoader favicons)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _favicons = favicons ?? throw new ArgumentNullException(nameof(favicons));
        _manager.Changed += (_, _) => Reconcile();
        Reconcile();
    }

    public ObservableCollection<object> Tiles { get; } = [];

    public bool TryAdd(string name, string value, out string? error)
    {
        if (!TryParseAddress(value, out var address) || address is null)
        {
            error = "Enter a valid web address.";
            return false;
        }

        return _manager.TryAdd(name, address, out error);
    }

    public bool TryUpdate(NewTabShortcutViewModel shortcut, string name, string value, out string? error)
    {
        if (!TryParseAddress(value, out var address) || address is null)
        {
            error = "Enter a valid web address.";
            return false;
        }

        return _manager.TryUpdate(shortcut.Id, name, address, out error);
    }

    public void Remove(NewTabShortcutViewModel shortcut) => _manager.Remove(shortcut.Id);

    private void Reconcile()
    {
        var wanted = _manager.Items.Select(item => item.Id).ToHashSet();

        foreach (var stale in _cache.Keys.Where(id => !wanted.Contains(id)).ToArray())
        {
            _cache.Remove(stale, out var viewModel);
            viewModel?.Dispose();
        }

        Tiles.Clear();

        foreach (var shortcut in _manager.Items)
        {
            if (!_cache.TryGetValue(shortcut.Id, out var viewModel))
            {
                viewModel = new NewTabShortcutViewModel(shortcut, _favicons);
                _cache.Add(shortcut.Id, viewModel);
            }
            else
            {
                viewModel.Refresh(shortcut);
            }

            Tiles.Add(viewModel);
        }

        if (_manager.Items.Count < ManageNewTabShortcuts.MaximumCount)
        {
            Tiles.Add(AddNewTabShortcutViewModel.Instance);
        }
    }

    private static bool TryParseAddress(string? value, out PageAddress? address)
    {
        address = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();

        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = "https://" + candidate;
        }

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
               PageAddress.TryCreate(uri, out address) &&
               address is not null;
    }
}
