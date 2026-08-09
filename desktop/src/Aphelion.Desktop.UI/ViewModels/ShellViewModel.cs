using System.Collections.ObjectModel;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// The window shell: the tab strip in the title bar, the side panel, and which
/// browser view is on screen.
/// </summary>
/// <remarks>
/// Every tab owns its own <see cref="BrowserViewModel"/>, because every tab needs
/// its own engine session and navigation history. The shell keeps them alive and
/// swaps which one is visible; it does not reuse a single view across tabs, which
/// would lose per-tab history.
/// </remarks>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly BrowsingSession _session = new();
    private readonly Func<BrowserViewModel> _browserFactory;
    private readonly Dictionary<TabId, BrowserViewModel> _browsers = [];

    public ShellViewModel(Func<BrowserViewModel> browserFactory, object? windowManager = null)
    {
        _browserFactory = browserFactory ?? throw new ArgumentNullException(nameof(browserFactory));
        WindowManager = windowManager;

        NewTab();
    }

    /// <summary>
    /// The window manager, held as <see cref="object"/> because it lives in the
    /// composition layer and the view models must not depend on views.
    /// </summary>
    public object? WindowManager { get; }

    /// <summary>True when this window has exactly one tab left.</summary>
    public bool IsSingleTab => _session.Tabs.Count <= 1;

    /// <summary>The address of a tab, for handing to a new window when torn off.</summary>
    public static PageAddress? AddressOf(TabItemViewModel item) => item?.Tab.Address;

    /// <summary>
    /// Removes a tab because it moved to another window. Unlike closing, this never
    /// opens a replacement tab: the caller is responsible for where the tab went.
    /// </summary>
    public void DetachTab(TabItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_browsers.Remove(item.Id, out var browser))
        {
            browser.PropertyChanged -= OnBrowserPropertyChanged;
        }

        _session.CloseTab(item.Id);
        SyncTabs();
    }

    /// <summary>Adds a tab received from another window and activates it.</summary>
    public void AdoptTab(PageAddress? address, int targetIndex)
    {
        var tab = _session.OpenTab(address);
        Attach(tab);
        _session.MoveTabTo(tab.Id, targetIndex, null);
        _session.Activate(tab.Id);
        SyncTabs();

        if (address is not null && _browsers.TryGetValue(tab.Id, out var browser))
        {
            browser.NavigateTo(address);
        }
    }

    /// <summary>Navigates the active tab, used when a new window opens on an address.</summary>
    public void NavigateActiveTab(PageAddress address)
    {
        if (ActiveBrowser is { } browser)
        {
            browser.NavigateTo(address);
        }
    }

    public ObservableCollection<TabItemViewModel> Tabs { get; } = [];

    [ObservableProperty]
    private TabItemViewModel? _activeTab;

    /// <summary>The browser view for the active tab, shown in the content area.</summary>
    [ObservableProperty]
    private BrowserViewModel? _activeBrowser;

    [ObservableProperty]
    private string _windowTitle = "Aphelion";

    [RelayCommand]
    private void NewTab()
    {
        var tab = _session.OpenTab();
        Attach(tab);
        SyncTabs();
    }

    [RelayCommand]
    private void CloseTab(TabItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (_browsers.Remove(item.Id, out var browser))
        {
            browser.PropertyChanged -= OnBrowserPropertyChanged;
        }

        _session.CloseTab(item.Id);

        // Closing the last tab opens a fresh one rather than leaving an empty
        // window: a browser with no tabs has nothing to show and no way back.
        if (_session.IsEmpty)
        {
            Attach(_session.OpenTab());
        }

        SyncTabs();
    }

    [RelayCommand]
    private void ActivateTab(TabItemViewModel? item)
    {
        if (item is null || !_session.Activate(item.Id))
        {
            return;
        }

        SyncTabs();
    }

    /// <summary>
    /// Drops a dragged tab at <paramref name="targetIndex"/>. The tab takes the
    /// group of whatever it lands among, so dragging into a group joins it and
    /// dragging past the group's edge leaves it — the Chrome behaviour.
    /// </summary>
    public void DropTab(TabItemViewModel item, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(item);

        var clamped = Math.Clamp(targetIndex, 0, Math.Max(0, _session.Tabs.Count - 1));
        _session.MoveTabTo(item.Id, clamped, GroupAt(clamped, item.Id));
        SyncTabs();
    }

    /// <summary>Drops a dragged group so its run starts at <paramref name="targetIndex"/>.</summary>
    public void DropGroup(TabGroupId groupId, int targetIndex)
    {
        _session.MoveGroup(groupId, targetIndex);
        SyncTabs();
    }

    /// <summary>
    /// The group a tab would belong to if dropped at <paramref name="index"/>: the
    /// group of its new neighbours, but only when both sides agree. Landing between
    /// a group and a loose tab leaves the tab ungrouped, which is how Chrome lets
    /// you drag out of a group without aiming at empty space.
    /// </summary>
    private TabGroupId? GroupAt(int index, TabId moving)
    {
        var others = _session.Tabs.Where(t => t.Id != moving).ToList();

        if (others.Count == 0)
        {
            return null;
        }

        var before = index > 0 && index - 1 < others.Count ? others[index - 1].GroupId : null;
        var after = index < others.Count ? others[index].GroupId : null;

        return before is not null && before == after ? before : null;
    }

    [RelayCommand]
    private void ToggleGroupCollapsed(TabGroupId groupId)
    {
        _session.FindGroup(groupId)?.ToggleCollapsed();
        SyncTabs();
    }

    [RelayCommand]
    private void CloseGroup(TabGroupId groupId)
    {
        foreach (var tab in _session.TabsInGroup(groupId))
        {
            if (_browsers.Remove(tab.Id, out var browser))
            {
                browser.PropertyChanged -= OnBrowserPropertyChanged;
            }
        }

        _session.CloseGroup(groupId);

        if (_session.IsEmpty)
        {
            Attach(_session.OpenTab());
        }

        SyncTabs();
    }

    [RelayCommand]
    private void UngroupTab(TabItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _session.RemoveFromGroup(item.Id);
        SyncTabs();
    }

    /// <summary>Groups the active tab, creating a group if it has none.</summary>
    [RelayCommand]
    private void GroupActiveTab()
    {
        if (_session.ActiveTab is not { } tab)
        {
            return;
        }

        if (tab.GroupId is not null)
        {
            _session.RemoveFromGroup(tab.Id);
        }
        else
        {
            var color = NextGroupColor();
            var group = _session.CreateGroup($"Group {_session.Groups.Count + 1}", color);
            _session.AddToGroup(tab.Id, group.Id);
        }

        SyncTabs();
    }

    /// <summary>Cycles the palette so consecutive groups are visually distinct.</summary>
    private GroupColor NextGroupColor()
    {
        var palette = Enum.GetValues<GroupColor>();
        return palette[_session.Groups.Count % palette.Length];
    }

    private void Attach(BrowserTab tab)
    {
        var browser = _browserFactory();
        browser.Bind(tab);
        browser.PropertyChanged += OnBrowserPropertyChanged;
        _browsers[tab.Id] = browser;
    }

    private void OnBrowserPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // A tab's title and loading state change as pages load, so the strip has to
        // follow rather than only refreshing when tabs open and close.
        if (e.PropertyName is nameof(BrowserViewModel.IsLoading)
            or nameof(BrowserViewModel.PageTitle))
        {
            RefreshTabDisplay();
        }
    }

    /// <summary>
    /// Brings the strip in line with the session and updates the visible browser.
    /// </summary>
    /// <remarks>
    /// Reconciles in place rather than clearing and refilling. Rebuilding the
    /// collection on every change would discard the item that a drag is currently
    /// holding, and would make the strip flash on each reorder.
    /// </remarks>
    private void SyncTabs()
    {
        var existing = Tabs.ToDictionary(t => t.Id);

        for (var index = 0; index < _session.Tabs.Count; index++)
        {
            var tab = _session.Tabs[index];

            if (!existing.TryGetValue(tab.Id, out var item))
            {
                item = new TabItemViewModel(tab);
            }

            var currentIndex = Tabs.IndexOf(item);

            if (currentIndex < 0)
            {
                Tabs.Insert(index, item);
            }
            else if (currentIndex != index)
            {
                Tabs.Move(currentIndex, index);
            }

            item.IsActive = _session.ActiveTab?.Id == tab.Id;
            item.Refresh(GroupColorOf(tab));
        }

        // Anything left over was closed.
        for (var index = Tabs.Count - 1; index >= _session.Tabs.Count; index--)
        {
            Tabs.RemoveAt(index);
        }

        ActiveTab = Tabs.FirstOrDefault(t => t.IsActive);
        ActiveBrowser = ActiveTab is null ? null : _browsers.GetValueOrDefault(ActiveTab.Id);

        WindowTitle = ActiveTab is null ? "Aphelion" : $"{ActiveTab.Title} — Aphelion";
    }

    private void RefreshTabDisplay()
    {
        foreach (var item in Tabs)
        {
            item.Refresh(GroupColorOf(item.Tab));
        }

        if (ActiveTab is not null)
        {
            WindowTitle = $"{ActiveTab.Title} — Aphelion";
        }
    }

    private GroupColor? GroupColorOf(BrowserTab tab) =>
        tab.GroupId is { } id ? _session.FindGroup(id)?.Color : null;
}
