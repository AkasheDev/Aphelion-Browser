using System.Collections.ObjectModel;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// The window shell: the tab strip in the title bar, the split view, and which
/// browser views are on screen.
/// </summary>
/// <remarks>
/// Every tab owns its own <see cref="BrowserViewModel"/>, because every tab needs
/// its own engine session and navigation history. The strip is a mixed sequence:
/// a <see cref="GroupHeaderViewModel"/> chip precedes each group's run of tabs,
/// as in Chrome, and a collapsed group shows only its chip.
/// </remarks>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly BrowsingSession _session = new();
    private readonly Func<BrowserViewModel> _browserFactory;
    private readonly Dictionary<TabId, BrowserViewModel> _browsers = [];
    private readonly Dictionary<TabId, TabItemViewModel> _tabItems = [];
    private readonly Dictionary<TabGroupId, GroupHeaderViewModel> _headers = [];
    private readonly ISessionStore? _sessionStore;
    private readonly IFaviconLoader? _favicons;

    private TabId? _splitTabId;

    public ShellViewModel(
        Func<BrowserViewModel> browserFactory,
        object? windowManager = null,
        ISessionStore? sessionStore = null,
        IFaviconLoader? favicons = null)
    {
        _browserFactory = browserFactory ?? throw new ArgumentNullException(nameof(browserFactory));
        WindowManager = windowManager;
        _sessionStore = sessionStore;
        _favicons = favicons;

        if (!TryRestore())
        {
            NewTab();
        }
    }

    /// <summary>
    /// The window manager, held as <see cref="object"/> because it lives in the
    /// composition layer and the view models must not depend on views.
    /// </summary>
    public object? WindowManager { get; }

    /// <summary>Tabs and group chips, in the order the strip draws them.</summary>
    public ObservableCollection<object> StripItems { get; } = [];

    [ObservableProperty]
    private TabItemViewModel? _activeTab;

    /// <summary>The browser view for the active tab, shown in the content area.</summary>
    [ObservableProperty]
    private BrowserViewModel? _activeBrowser;

    /// <summary>The browser shown beside the active one while split view is on.</summary>
    [ObservableProperty]
    private BrowserViewModel? _splitBrowser;

    [ObservableProperty]
    private bool _isSplit;

    [ObservableProperty]
    private string _windowTitle = "Aphelion";

    /// <summary>True when this window has exactly one tab left.</summary>
    public bool IsSingleTab => _session.Tabs.Count <= 1;

    /// <summary>The address of a tab, for handing to a new window when torn off.</summary>
    public static PageAddress? AddressOf(TabItemViewModel item) => item?.Tab.Address;

    /// <summary>The tab's position in the session, which is the order the user sees.</summary>
    public int IndexOfTab(TabItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        for (var i = 0; i < _session.Tabs.Count; i++)
        {
            if (_session.Tabs[i].Id == item.Id)
            {
                return i;
            }
        }

        return -1;
    }

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

        Detach(item.Id);
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

        // Activating the split partner would put one view model in two panes;
        // the split closes instead, and the tab takes the whole window.
        if (_splitTabId == item.Id)
        {
            _splitTabId = null;
        }

        SyncTabs();
    }

    /// <summary>
    /// Drops a dragged tab at <paramref name="targetIndex"/>, joining
    /// <paramref name="group"/> when one is given. The group comes from the view:
    /// whatever tab or chip the pointer is over decides membership, which is how
    /// Chrome lets a tab join a single-member group — a neighbour-based rule can
    /// never fire there.
    /// </summary>
    public void DropTab(TabItemViewModel item, int targetIndex, TabGroupId? group)
    {
        ArgumentNullException.ThrowIfNull(item);

        var clamped = Math.Clamp(targetIndex, 0, Math.Max(0, _session.Tabs.Count - 1));
        _session.MoveTabTo(item.Id, clamped, group);
        SyncTabs();
    }

    /// <summary>Drops a dragged group so its run starts at <paramref name="targetIndex"/>.</summary>
    public void DropGroup(TabGroupId groupId, int targetIndex)
    {
        _session.MoveGroup(groupId, targetIndex);
        SyncTabs();
    }

    [RelayCommand]
    private void ToggleGroupCollapsed(TabGroupId groupId)
    {
        var group = _session.FindGroup(groupId);

        if (group is null)
        {
            return;
        }

        group.ToggleCollapsed();

        // Collapsing the last visible run would leave the strip empty.
        if (group.IsCollapsed && _session.Tabs.All(IsHidden))
        {
            group.Expand();
        }

        SyncTabs();
    }

    [RelayCommand]
    private void CloseGroup(TabGroupId groupId)
    {
        foreach (var tab in _session.TabsInGroup(groupId))
        {
            Detach(tab.Id);
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

    /// <summary>Shows <paramref name="item"/>'s page beside the active tab's.</summary>
    [RelayCommand]
    private void SplitWithTab(TabItemViewModel? item)
    {
        if (item is null || _session.ActiveTab?.Id == item.Id)
        {
            return;
        }

        _splitTabId = item.Id;
        SyncTabs();
    }

    [RelayCommand]
    private void CloseSplit()
    {
        _splitTabId = null;
        SyncTabs();
    }

    /// <summary>
    /// Removes a tab because it moved to another window. Unlike closing, this never
    /// opens a replacement tab: the caller is responsible for where the tab went.
    /// </summary>
    public void DetachTab(TabItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Detach(item.Id);
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
        ActiveBrowser?.NavigateTo(address);
    }

    /// <summary>The open-tab state to persist at shutdown.</summary>
    public SessionSnapshot CaptureSnapshot()
    {
        var tabs = new List<SessionTabSnapshot>();
        var activeIndex = 0;

        for (var i = 0; i < _session.Tabs.Count; i++)
        {
            var tab = _session.Tabs[i];
            var group = tab.GroupId is { } id ? _session.FindGroup(id) : null;

            if (_session.ActiveTab?.Id == tab.Id)
            {
                activeIndex = i;
            }

            tabs.Add(new SessionTabSnapshot(
                tab.Address?.ToString(),
                group?.Name,
                group?.Color.ToString(),
                group?.IsCollapsed ?? false));
        }

        return new SessionSnapshot(tabs, activeIndex);
    }

    /// <summary>
    /// Rebuilds the previous session, returning false when there is nothing to
    /// restore so the caller opens a fresh tab instead.
    /// </summary>
    private bool TryRestore()
    {
        if (_sessionStore?.Load() is not { Tabs.Count: > 0 } snapshot)
        {
            return false;
        }

        var groups = new Dictionary<string, TabGroup>();

        foreach (var saved in snapshot.Tabs)
        {
            PageAddress? address = null;

            if (saved.Address is not null &&
                Uri.TryCreate(saved.Address, UriKind.Absolute, out var uri))
            {
                PageAddress.TryCreate(uri, out address);
            }

            var tab = _session.OpenTab(address, activate: false);
            Attach(tab);

            if (saved.GroupName is not null)
            {
                var key = $"{saved.GroupName}|{saved.GroupColor}";

                if (!groups.TryGetValue(key, out var group))
                {
                    var color = Enum.TryParse<GroupColor>(saved.GroupColor, out var parsed)
                        ? parsed
                        : GroupColor.Slate;
                    group = _session.CreateGroup(saved.GroupName, color);
                    groups[key] = group;
                }

                _session.AddToGroup(tab.Id, group.Id);

                if (saved.GroupCollapsed)
                {
                    group.Collapse();
                }
            }

            // Marks the tab as loading so the navigation replays once its view
            // attaches an engine session.
            if (address is not null)
            {
                _browsers[tab.Id].NavigateTo(address);
            }
        }

        var index = Math.Clamp(snapshot.ActiveIndex, 0, _session.Tabs.Count - 1);
        _session.Activate(_session.Tabs[index].Id);
        SyncTabs();
        return true;
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

    private void Detach(TabId id)
    {
        if (_browsers.Remove(id, out var browser))
        {
            browser.PropertyChanged -= OnBrowserPropertyChanged;
        }
    }

    private void OnBrowserPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // A tab's title and loading state change as pages load, so the strip has to
        // follow rather than only refreshing when tabs open and close.
        if (e.PropertyName is nameof(BrowserViewModel.IsLoading)
            or nameof(BrowserViewModel.PageTitle)
            or nameof(BrowserViewModel.FaviconAddress))
        {
            RefreshTabDisplay();
        }
    }

    private bool IsHidden(BrowserTab tab) =>
        tab.GroupId is { } id && _session.FindGroup(id)?.IsCollapsed == true;

    /// <summary>
    /// Brings the strip in line with the session and updates the visible browsers.
    /// </summary>
    /// <remarks>
    /// Reconciles in place rather than clearing and refilling: rebuilding the
    /// collection would discard the item a drag is holding and flash the strip.
    /// </remarks>
    private void SyncTabs()
    {
        // A collapsed group hides its tabs; the active tab must stay visible.
        if (_session.ActiveTab is { } hiddenActive && IsHidden(hiddenActive))
        {
            var visible = _session.Tabs.FirstOrDefault(t => !IsHidden(t));

            if (visible is not null)
            {
                _session.Activate(visible.Id);
            }
        }

        var desired = new List<object>();
        var seenGroups = new HashSet<TabGroupId>();
        TabGroupId? run = null;

        foreach (var tab in _session.Tabs)
        {
            if (tab.GroupId is { } groupId)
            {
                if (run != groupId)
                {
                    run = groupId;

                    // Each chip appears once even if a group were ever split across
                    // the strip — a duplicate instance would corrupt the in-place
                    // reconcile below and crash.
                    if (seenGroups.Add(groupId))
                    {
                        desired.Add(HeaderFor(groupId));
                    }
                }

                if (_session.FindGroup(groupId)?.IsCollapsed == true)
                {
                    continue;
                }
            }
            else
            {
                run = null;
            }

            desired.Add(ItemFor(tab));
        }

        for (var i = StripItems.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(StripItems[i]))
            {
                StripItems.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var index = StripItems.IndexOf(desired[i]);

            if (index < 0)
            {
                StripItems.Insert(i, desired[i]);
            }
            else if (index != i)
            {
                StripItems.Move(index, i);
            }
        }

        PruneCaches();
        RefreshTabDisplay();

        ActiveTab = _session.ActiveTab is { } active
            ? _tabItems.GetValueOrDefault(active.Id)
            : null;
        ActiveBrowser = ActiveTab is null ? null : _browsers.GetValueOrDefault(ActiveTab.Id);

        // Split housekeeping: the partner may have closed or become the active tab.
        if (_splitTabId is { } split &&
            (_session.Tabs.All(t => t.Id != split) || _session.ActiveTab?.Id == split))
        {
            _splitTabId = null;
        }

        SplitBrowser = _splitTabId is { } partner ? _browsers.GetValueOrDefault(partner) : null;
        IsSplit = SplitBrowser is not null;
    }

    private void RefreshTabDisplay()
    {
        foreach (var tab in _session.Tabs)
        {
            if (_tabItems.TryGetValue(tab.Id, out var item))
            {
                item.IsActive = _session.ActiveTab?.Id == tab.Id;
                item.Refresh(GroupColorOf(tab));
            }
        }

        foreach (var (id, header) in _headers)
        {
            if (_session.FindGroup(id) is { } group)
            {
                header.Refresh(group);
            }
        }

        WindowTitle = _session.ActiveTab is { } active
            ? $"{active.DisplayTitle} — Aphelion"
            : "Aphelion";
    }

    private TabItemViewModel ItemFor(BrowserTab tab)
    {
        if (!_tabItems.TryGetValue(tab.Id, out var item))
        {
            item = new TabItemViewModel(tab, _favicons);
            _tabItems[tab.Id] = item;
        }

        return item;
    }

    private GroupHeaderViewModel HeaderFor(TabGroupId id)
    {
        if (_headers.TryGetValue(id, out var header))
        {
            return header;
        }

        header = new GroupHeaderViewModel(
            id,
            rename: name =>
            {
                _session.FindGroup(id)?.Rename(name);
                SyncTabs();
            },
            recolor: color =>
            {
                _session.FindGroup(id)?.Recolor(color);
                SyncTabs();
            },
            toggle: () => ToggleGroupCollapsed(id),
            ungroup: () =>
            {
                foreach (var tab in _session.TabsInGroup(id))
                {
                    _session.RemoveFromGroup(tab.Id);
                }

                SyncTabs();
            },
            close: () => CloseGroup(id));

        _headers[id] = header;
        return header;
    }

    private void PruneCaches()
    {
        foreach (var id in _tabItems.Keys.Where(k => _session.Tabs.All(t => t.Id != k)).ToList())
        {
            _tabItems.Remove(id);
        }

        foreach (var id in _headers.Keys.Where(k => _session.FindGroup(k) is null).ToList())
        {
            _headers.Remove(id);
        }
    }

    private GroupColor? GroupColorOf(BrowserTab tab) =>
        tab.GroupId is { } id ? _session.FindGroup(id)?.Color : null;
}
