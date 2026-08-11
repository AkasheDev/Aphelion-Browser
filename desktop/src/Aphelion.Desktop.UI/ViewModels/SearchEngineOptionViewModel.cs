using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Enums;
using Aphelion.Desktop.Domain.ValueObjects;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aphelion.Desktop.UI.ViewModels;

public sealed partial class SearchEngineOptionViewModel : ViewModelBase, IDisposable
{
    public SearchEngineOptionViewModel(
        SearchEngineKind kind,
        string name,
        string homeAddress,
        IFaviconLoader favicons)
    {
        Kind = kind;
        Name = name;
        FallbackText = name[..1];
        LoadIcon(homeAddress, favicons);
    }

    public SearchEngineKind Kind { get; }

    public string Name { get; }

    public string FallbackText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private Bitmap? _icon;

    public bool HasIcon => Icon is not null;

    public void Dispose()
    {
        Icon?.Dispose();
        Icon = null;
    }

    private async void LoadIcon(string homeAddress, IFaviconLoader favicons)
    {
        var faviconUri = new Uri(new Uri(homeAddress), "/favicon.ico");

        if (!PageAddress.TryCreate(faviconUri, out var address) || address is null)
        {
            return;
        }

        var bytes = await favicons.LoadAsync(address.Value).ConfigureAwait(true);

        if (bytes is null)
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            Icon = new Bitmap(stream);
        }
        catch (Exception)
        {
            // The compact monogram remains when a provider rejects its favicon.
        }
    }
}
