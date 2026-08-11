using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>Presentation state for one favicon-backed New Tab launcher.</summary>
public sealed partial class NewTabShortcutViewModel : ViewModelBase, IDisposable
{
    private readonly IFaviconLoader _favicons;
    private string? _loadedAddress;

    public NewTabShortcutViewModel(NewTabShortcut shortcut, IFaviconLoader favicons)
    {
        _favicons = favicons ?? throw new ArgumentNullException(nameof(favicons));
        Id = shortcut.Id;
        Address = shortcut.Address;
        Name = shortcut.Name;
        LoadFaviconIfNeeded();
    }

    public NewTabShortcutId Id { get; }

    [ObservableProperty]
    private PageAddress _address;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FallbackText))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFavicon))]
    private Bitmap? _favicon;

    public bool HasFavicon => Favicon is not null;

    public string FallbackText => string.IsNullOrWhiteSpace(Name)
        ? "?"
        : Name[..1].ToUpperInvariant();

    public void Refresh(NewTabShortcut shortcut)
    {
        Name = shortcut.Name;

        if (!Address.Equals(shortcut.Address))
        {
            Address = shortcut.Address;
            Favicon?.Dispose();
            Favicon = null;
            _loadedAddress = null;
            LoadFaviconIfNeeded();
        }
    }

    public void Dispose()
    {
        Favicon?.Dispose();
        Favicon = null;
    }

    private async void LoadFaviconIfNeeded()
    {
        var key = Address.ToString();

        if (_loadedAddress == key)
        {
            return;
        }

        _loadedAddress = key;

        var faviconUri = new UriBuilder(Address.Value)
        {
            Path = "/favicon.ico",
            Query = string.Empty,
            Fragment = string.Empty,
        }.Uri;

        if (!PageAddress.TryCreate(faviconUri, out var faviconAddress) || faviconAddress is null)
        {
            return;
        }

        var bytes = await _favicons.LoadAsync(faviconAddress.Value).ConfigureAwait(true);

        if (bytes is null || _loadedAddress != key)
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            Favicon = new Bitmap(stream);
        }
        catch (Exception)
        {
            // A letter tile remains when a site serves an unsupported icon.
        }
    }
}
