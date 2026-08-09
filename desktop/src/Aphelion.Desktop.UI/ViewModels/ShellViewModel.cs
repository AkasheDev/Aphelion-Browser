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

    /// <summary>
    /// How many tabs the strip can show at once, reported by the panel as it
    /// lays out. Anything past this spills into the overflow panel.
    /// </summary>
    private int _visibleCapacity = int.MaxValue;

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

        Overflow = new TabListViewModel("Other tabs", item =>
        {
            ActivateTab(item);
            IsOverflowOpen = false;
        });

        SplitPicker = new TabListViewModel(
            "Choose a tab to split with",
            item =>
            {
                IsSplitPickerOpen = false;
                SplitWithTab(item);
            },
            createNew: () =>
            {
                IsSplitPickerOpen = false;
                SplitWithNewTab();
            });

        if (!TryRestore())
        {
            NewTab();
        }
    }

    /// <summary>Tabs that did not fit the strip, listed a page at a time.</summary>
    public TabListViewModel Overflow { get; }

    /// <summary>Candidates for the second pane, listed a page at a time.</summary>
    public TabListViewModel SplitPicker { get; }

    [ObservableProperty]
    private bool _isOverflowOpen;

    [ObservableProperty]
    private bool _isSplitPickerOpen;

    /// <summary>True when tabs had to be pushed into the overflow panel.</summary>
    [ObservableProperty]
    private bool _hasOverflow;

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
        if (item is null || _session.ActiveTab is not { } active || active.Id == item.Id)
        {
            return;
        }

        _session.Split(active.Id, item.Id);
        SyncTabs();
    }

    /// <summary>
    /// Turns split view on, or off when the active tab is already split. Opening
    /// asks which tab to pair with rather than guessing, as Chrome does; with no
    /// other tab to offer, it opens a blank one instead of showing an empty
    /// picker.
    /// </summary>
    [RelayCommand]
    private void ToggleSplit()
    {
        if (_session.ActiveTab is not { } active)
        {
            return;
        }

        if (active.SplitPartnerId is not null)
        {
            _session.Unsplit(active.Id);
            SyncTabs();
            return;
        }

        // Chrome opens the split first and asks what fills the second pane
        // afterwards, with the picker sitting in that empty pane. The picker's
        // "New tab" row covers the case where there is nothing to choose.
        var candidates = _session.VisibleTabs
            .Where(t => t.Id != active.Id && !IsHidden(t))
            .ToList();

        SplitPicker.SetItems(candidates.Select(ItemFor));
        IsSplitPickerOpen = true;
        SyncTabs();
    }

    /// <summary>Splits the active tab with a freshly opened blank one.</summary>
    private void SplitWithNewTab()
    {
        if (_session.ActiveTab is not { } active)
        {
            return;
        }

        var partner = _session.OpenTabNextTo(active, activate: false);
        Attach(partner);
        _session.Split(active.Id, partner.Id);
        SyncTabs();
    }

    /// <summary>
    /// Dismisses the picker. The second pane closes with it unless a partner was
    /// chosen, since an empty pane with no picker in it serves no purpose.
    /// </summary>
    [RelayCommand]
    private void CloseSplitPicker()
    {
        IsSplitPickerOpen = false;
        SyncTabs();
    }

    [RelayCommand]
    private void ToggleOverflow() => IsOverflowOpen = !IsOverflowOpen;

    [RelayCommand]
    private void CloseOverflow() => IsOverflowOpen = false;

    /// <summary>
    /// Told by the strip how many tabs it can show. Anything beyond that is
    /// listed in the overflow panel instead of being opened where the user
    /// cannot reach it.
    /// </summary>
    public void ReportStripCapacity(int capacity)
    {
        var clamped = Math.Max(1, capacity);

        if (clamped == _visibleCapacity)
        {
            return;
        }

        _visibleCapacity = clamped;
        SyncTabs();
    }

    [RelayCommand]
    private void CloseSplit()
    {
        if (_session.ActiveTab is { } active)
        {
            _session.Unsplit(active.Id);
            SyncTabs();
        }
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

        // Only tabs that fit go in the strip; the rest are listed in the overflow
        // panel. Opening tabs the user cannot reach would strand them.
        var listed = _session.VisibleTabs.Where(t => !IsHidden(t)).ToList();
        var shown = listed.Take(_visibleCapacity).ToList();
        var overflowed = listed.Skip(_visibleCapacity).ToList();

        // The active tab is always reachable in the strip, even if it sorts past
        // the capacity — swap it in for the last visible one.
        if (_session.ActiveTab is { } current &&
            overflowed.Any(t => t.Id == current.Id) &&
            shown.Count > 0)
        {
            overflowed.Remove(current);
            overflowed.Insert(0, shown[^1]);
            shown[^1] = current;
        }

        var visibleIds = shown.Select(t => t.Id).ToHashSet();

        Overflow.SetItems(overflowed.Select(ItemFor));
        HasOverflow = overflowed.Count > 0;

        if (!HasOverflow)
        {
            IsOverflowOpen = false;
        }

        var desired = new List<object>();
        var seenGroups = new HashSet<TabGroupId>();
        TabGroupId? run = null;

        foreach (var tab in _session.Tabs)
        {
            if (tab.IsSplitPartner || !visibleIds.Contains(tab.Id))
            {
                continue;
            }

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

        // The pairing lives on the tab, so the second pane simply follows it.
        SplitBrowser = _session.ActiveTab?.SplitPartnerId is { } partner
            ? _browsers.GetValueOrDefault(partner)
            : null;

        // The second pane is also open while the picker is choosing what goes in
        // it, which is where the picker appears.
        IsSplit = SplitBrowser is not null || IsSplitPickerOpen;
    }

    private void RefreshTabDisplay()
    {
        foreach (var tab in _session.Tabs)
        {
            if (_tabItems.TryGetValue(tab.Id, out var item))
            {
                item.IsActive = _session.ActiveTab?.Id == tab.Id;

                var partner = tab.SplitPartnerId is { } id
                    ? _session.Tabs.FirstOrDefault(t => t.Id == id)
                    : null;

                item.Refresh(GroupColorOf(tab), partner);
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
