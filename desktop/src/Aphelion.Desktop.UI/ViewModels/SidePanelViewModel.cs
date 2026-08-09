using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>Which tool the side panel is showing.</summary>
public enum SidePanelTool
{
    None,
    Tabs,
    History,
    Bookmarks,
    Downloads,
}

/// <summary>
/// The Opera GX style side rail: a strip of tool buttons that expands into a panel.
/// </summary>
public sealed partial class SidePanelViewModel : ViewModelBase
{
    [ObservableProperty]
    private SidePanelTool _activeTool = SidePanelTool.None;

    public bool IsExpanded => ActiveTool != SidePanelTool.None;

    public string Header => ActiveTool switch
    {
        SidePanelTool.Tabs => "Tabs",
        SidePanelTool.History => "History",
        SidePanelTool.Bookmarks => "Bookmarks",
        SidePanelTool.Downloads => "Downloads",
        _ => string.Empty,
    };

    /// <summary>
    /// True for tools whose backing feature does not exist yet, so the panel can say
    /// so plainly instead of showing a convincing but empty list.
    /// </summary>
    public bool IsPlaceholder => ActiveTool is SidePanelTool.History
        or SidePanelTool.Bookmarks
        or SidePanelTool.Downloads;

    partial void OnActiveToolChanged(SidePanelTool value)
    {
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(IsPlaceholder));
    }

    /// <summary>Clicking the active tool closes the panel, as in Opera GX.</summary>
    [RelayCommand]
    private void Toggle(SidePanelTool tool) =>
        ActiveTool = ActiveTool == tool ? SidePanelTool.None : tool;

    [RelayCommand]
    private void Close() => ActiveTool = SidePanelTool.None;
}
