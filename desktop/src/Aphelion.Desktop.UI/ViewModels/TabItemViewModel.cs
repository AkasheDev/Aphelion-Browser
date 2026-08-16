using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain;
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
public sealed partial class TabItemViewModel(
    ShellViewModel owner,
    BrowserTab tab,
    IFaviconLoader? favicons = null) : ViewModelBase
{
    private readonly IFaviconLoader? _favicons = favicons;

    /// <summary>
    /// Commands shared by the strip and detached popup menus. Avalonia context
    /// menus live in their own popup tree, so ancestor/name bindings cannot reach
    /// the shell reliably; the row carries its command owner explicitly.
    /// </summary>
    public ShellViewModel Owner { get; } = owner ?? throw new ArgumentNullException(nameof(owner));

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

    /// <summary>
    /// Places the rendered tab in its closed pose. Existing tabs transition into
    /// this pose before removal; newly inserted tabs start here and transition
    /// back out of it, making close the exact reverse of open.
    /// </summary>
    [ObservableProperty]
    private bool _isExiting;

    /// <summary>Whether the active tab can pair with this ordinary tab.</summary>
    public bool CanSplitWithActive => !IsActive && !IsSplit;

    partial void OnIsActiveChanged(bool value) =>
        OnPropertyChanged(nameof(CanSplitWithActive));

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

    /// <summary>The split partner's title, shown after its own favicon.</summary>
    [ObservableProperty]
    private string _partnerTitle = string.Empty;

    /// <summary>True when the partner's placeholder glyph should stand in for a
    /// missing icon — only meaningful while this tab is split.</summary>
    public bool ShowsPartnerFallback => IsSplit && PartnerFavicon is null;

    partial void OnFaviconChanged(Bitmap? value) =>
        OnPropertyChanged(nameof(ShowsGlobeFallback));

    /// <summary>
    /// Column widths for the tab's contents. The two titles share the space when
    /// split; otherwise the partner's column takes none, so an ordinary tab does
    /// not leave half its width blank.
    /// </summary>
    public string TitleColumns => IsSplit
        ? "Auto,Auto,*,Auto,Auto,*,Auto"
        : "Auto,Auto,*,Auto,Auto,Auto,Auto";

    partial void OnIsSplitChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowsPartnerFallback));
        OnPropertyChanged(nameof(TitleColumns));
        OnPropertyChanged(nameof(CanSplitWithActive));
    }

    partial void OnPartnerFaviconChanged(Bitmap? value) =>
        OnPropertyChanged(nameof(ShowsPartnerFallback));

    /// <summary>
    /// Brush for the owning group's colour, or null when ungrouped.
    /// </summary>
    [ObservableProperty]
    private IBrush? _groupBrush;

    public bool IsGrouped => GroupBrush is not null;

    partial void OnGroupBrushChanged(IBrush? value) => OnPropertyChanged(nameof(IsGrouped));

    public bool IsDownloadsPage => Tab.IsDownloadsPage;

    public bool ShowsGlobeFallback => Favicon is null && !IsDownloadsPage;

    /// <summary>Pulls display state back from the domain tab.</summary>
    public void Refresh(GroupColor? groupColor, BrowserTab? partner = null)
    {
        IsSplit = Tab.SplitPartnerId is not null;

        // A split pair reads as "favicon name / favicon name": each half's icon
        // sits directly before its own title.
        Title = Tab.DisplayTitle;
        PartnerTitle = partner?.DisplayTitle ?? string.Empty;

        IsLoading = Tab.LoadState == TabLoadState.Loading;
        GroupBrush = groupColor is null ? null : GroupBrushes.For(groupColor.Value);

        OnPropertyChanged(nameof(IsDownloadsPage));
        OnPropertyChanged(nameof(ShowsGlobeFallback));

        LoadFaviconIfChanged();
        LoadPartnerFaviconIfChanged(partner);
    }

    private async void LoadFaviconIfChanged()
    {
        if (Tab.IsDownloadsPage)
        {
            _loadedIconKey = InternalPages.DownloadsAddress;
            Favicon = null;
            return;
        }

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
