using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// One tab in the title-bar strip. A view over <see cref="BrowserTab"/>; the tab
/// itself remains the source of truth.
/// </summary>
public sealed partial class TabItemViewModel(BrowserTab tab, IFaviconLoader? favicons = null) : ViewModelBase
{
    private readonly IFaviconLoader? _favicons = favicons;

    /// <summary>The icon address currently loaded, so it is fetched only once.</summary>
    private string? _loadedIconKey;

    /// <summary>The same, for the split partner's icon.</summary>
    private string? _loadedPartnerIconKey;

    public BrowserTab Tab { get; } = tab ?? throw new ArgumentNullException(nameof(tab));

    public TabId Id => Tab.Id;

    [ObservableProperty]
    private string _title = tab.DisplayTitle;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>The page's icon, or null while it loads or when the site has none.</summary>
    [ObservableProperty]
    private Bitmap? _favicon;

    /// <summary>
    /// The split partner's icon. A split pair occupies one tab, but the tab shows
    /// both icons so it is visibly a pair rather than an ordinary tab.
    /// </summary>
    [ObservableProperty]
    private Bitmap? _partnerFavicon;

    /// <summary>True when this tab is the visible half of a split pair.</summary>
    [ObservableProperty]
    private bool _isSplit;

    /// <summary>
    /// Brush for the owning group's colour, or null when ungrouped.
    /// </summary>
    [ObservableProperty]
    private IBrush? _groupBrush;

    public bool IsGrouped => GroupBrush is not null;

    partial void OnGroupBrushChanged(IBrush? value) => OnPropertyChanged(nameof(IsGrouped));

    /// <summary>Pulls display state back from the domain tab.</summary>
    public void Refresh(GroupColor? groupColor, BrowserTab? partner = null)
    {
        IsSplit = Tab.SplitPartnerId is not null;

        // A split pair reads as one tab carrying two pages, so the label names
        // both rather than hiding that the second one is there.
        Title = IsSplit && partner is not null
            ? $"{Tab.DisplayTitle}  |  {partner.DisplayTitle}"
            : Tab.DisplayTitle;

        IsLoading = Tab.LoadState == TabLoadState.Loading;
        GroupBrush = groupColor is null ? null : GroupBrushes.For(groupColor.Value);

        LoadFaviconIfChanged();
        LoadPartnerFaviconIfChanged(partner);
    }

    private async void LoadFaviconIfChanged()
    {
        var key = Tab.FaviconAddress?.ToString();

        if (key == _loadedIconKey)
        {
            return;
        }

        _loadedIconKey = key;
        Favicon = await FetchAsync(Tab.FaviconAddress, () => _loadedIconKey == key);
    }

    private async void LoadPartnerFaviconIfChanged(BrowserTab? partner)
    {
        var key = partner?.FaviconAddress?.ToString();

        if (key == _loadedPartnerIconKey)
        {
            return;
        }

        _loadedPartnerIconKey = key;
        PartnerFavicon = await FetchAsync(partner?.FaviconAddress, () => _loadedPartnerIconKey == key);
    }

    /// <summary>
    /// Fetches and decodes an icon, discarding the result if the tab moved on
    /// while it was in flight.
    /// </summary>
    private async Task<Bitmap?> FetchAsync(PageAddress? address, Func<bool> stillWanted)
    {
        if (address is null || _favicons is null)
        {
            return null;
        }

        var bytes = await _favicons.LoadAsync(address.Value).ConfigureAwait(true);

        if (bytes is null || !stillWanted())
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            // Not every favicon is a format Avalonia can decode — .ico with
            // unusual encodings in particular. The fallback glyph covers it.
            return null;
        }
    }
}

/// <summary>Maps the domain's closed colour set to brushes.</summary>
public static class GroupBrushes
{
    private static readonly IBrush Violet = new SolidColorBrush(Color.FromRgb(0x8C, 0x83, 0xFF));
    private static readonly IBrush Cyan = new SolidColorBrush(Color.FromRgb(0x3F, 0xD8, 0xE4));
    private static readonly IBrush Emerald = new SolidColorBrush(Color.FromRgb(0x3F, 0xD9, 0xA0));
    private static readonly IBrush Amber = new SolidColorBrush(Color.FromRgb(0xF0, 0xB8, 0x49));
    private static readonly IBrush Rose = new SolidColorBrush(Color.FromRgb(0xF2, 0x68, 0x8A));
    private static readonly IBrush Slate = new SolidColorBrush(Color.FromRgb(0x8A, 0x90, 0xA8));

    public static IBrush For(GroupColor color) => color switch
    {
        GroupColor.Violet => Violet,
        GroupColor.Cyan => Cyan,
        GroupColor.Emerald => Emerald,
        GroupColor.Amber => Amber,
        GroupColor.Rose => Rose,
        _ => Slate,
    };
}
