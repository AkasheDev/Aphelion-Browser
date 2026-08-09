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
    /// Brush for the owning group's colour, or null when ungrouped.
    /// </summary>
    [ObservableProperty]
    private IBrush? _groupBrush;

    public bool IsGrouped => GroupBrush is not null;

    partial void OnGroupBrushChanged(IBrush? value) => OnPropertyChanged(nameof(IsGrouped));

    /// <summary>Pulls display state back from the domain tab.</summary>
    public void Refresh(GroupColor? groupColor)
    {
        Title = Tab.DisplayTitle;
        IsLoading = Tab.LoadState == TabLoadState.Loading;
        GroupBrush = groupColor is null ? null : GroupBrushes.For(groupColor.Value);

        LoadFaviconIfChanged();
    }

    private async void LoadFaviconIfChanged()
    {
        var key = Tab.FaviconAddress?.ToString();

        if (key == _loadedIconKey)
        {
            return;
        }

        _loadedIconKey = key;
        Favicon = null;

        if (key is null || _favicons is null || Tab.FaviconAddress is not { } address)
        {
            return;
        }

        var bytes = await _favicons.LoadAsync(address.Value).ConfigureAwait(true);

        // The tab may have navigated on while the icon was fetched.
        if (bytes is null || _loadedIconKey != key)
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
            // Not every favicon is a format Avalonia can decode — .ico with
            // unusual encodings in particular. The fallback glyph covers it.
            Favicon = null;
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
