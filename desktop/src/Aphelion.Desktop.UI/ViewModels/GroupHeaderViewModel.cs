using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// The coloured chip that precedes a group's tabs in the strip, as in Chrome:
/// clicking it collapses or expands the group, and its menu renames, recolours,
/// ungroups or closes it.
/// </summary>
/// <remarks>
/// Behaviour is injected as delegates rather than the shell being referenced
/// directly, so the chip cannot reach anything beyond the operations it owns.
/// </remarks>
public sealed partial class GroupHeaderViewModel : ViewModelBase
{
    private readonly Action<string> _rename;
    private readonly Action<GroupColor> _recolor;
    private readonly Action _toggle;
    private readonly Action _ungroup;
    private readonly Action _close;

    /// <summary>Guards the two-way name binding against echoing a refresh back as a rename.</summary>
    private bool _refreshing;

    public GroupHeaderViewModel(
        TabGroupId id,
        Action<string> rename,
        Action<GroupColor> recolor,
        Action toggle,
        Action ungroup,
        Action close)
    {
        Id = id;
        _rename = rename ?? throw new ArgumentNullException(nameof(rename));
        _recolor = recolor ?? throw new ArgumentNullException(nameof(recolor));
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
        _ungroup = ungroup ?? throw new ArgumentNullException(nameof(ungroup));
        _close = close ?? throw new ArgumentNullException(nameof(close));
    }

    public TabGroupId Id { get; }

    [ObservableProperty]
    private string _name = "Group";

    [ObservableProperty]
    private IBrush _brush = GroupBrushes.For(GroupColor.Slate);

    [ObservableProperty]
    private bool _isCollapsed;

    partial void OnNameChanged(string value)
    {
        if (!_refreshing)
        {
            _rename(value);
        }
    }

    [RelayCommand]
    private void ToggleCollapsed() => _toggle();

    [RelayCommand]
    private void Recolor(GroupColor color) => _recolor(color);

    [RelayCommand]
    private void Ungroup() => _ungroup();

    [RelayCommand]
    private void CloseGroup() => _close();

    public void Refresh(TabGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        _refreshing = true;
        Name = group.Name;
        IsCollapsed = group.IsCollapsed;
        Brush = GroupBrushes.For(group.Color);
        _refreshing = false;
    }
}
