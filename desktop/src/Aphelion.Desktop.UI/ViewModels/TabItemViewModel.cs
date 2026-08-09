using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// One tab in the title-bar strip. A view over <see cref="BrowserTab"/>; the tab
/// itself remains the source of truth.
/// </summary>
public sealed partial class TabItemViewModel(BrowserTab tab) : ViewModelBase
{
    public BrowserTab Tab { get; } = tab ?? throw new ArgumentNullException(nameof(tab));

    public TabId Id => Tab.Id;

    [ObservableProperty]
    private string _title = tab.DisplayTitle;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Brush for the owning group's colour, or null when ungrouped.
    /// </summary>
    /// <remarks>
    /// Resolved to a concrete brush here rather than bound through a key: the domain
    /// keeps a closed colour set, and this is the single place that maps it to
    /// presentation. One mapping beats a converter plus a key lookup at every tab.
    /// </remarks>
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
