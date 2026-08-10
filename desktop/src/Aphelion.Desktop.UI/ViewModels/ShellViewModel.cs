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
    /// How much room the strip has, reported by the panel as it lays out. What
    /// does not fit spills into the overflow panel.
    /// </summary>
    private double _stripRoom = double.PositiveInfinity;

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

        Overflow = new TabListViewModel(
            "Other tabs",
            item =>
            {
                ActivateTab(item);
                IsOverflowOpen = false;
            },
            close: CloseTab,
            owner: this);

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

    /// <summary>The browser in the left pane.</summary>
    [ObservableProperty]
    private BrowserViewModel? _activeBrowser;

    /// <summary>The browser in the right pane while split view is on.</summary>
    [ObservableProperty]
    private BrowserViewModel? _splitBrowser;

    [ObservableProperty]
    private bool _isSplit;

    /// <summary>
    /// True when the right pane holds the focus. Chrome gives one pane of a split
    /// the focus at a time: the toolbar drives that pane, and clicking the other
    /// hands it over.
    /// </summary>
    [ObservableProperty]
    private bool _isRightPaneFocused;

    /// <summary>
    /// The browser the toolbar acts on: the focused pane, which is the left one
    /// unless the user clicked into the right.
    /// </summary>
    public BrowserViewModel? FocusedBrowser =>
        IsSplit && IsRightPaneFocused ? SplitBrowser : ActiveBrowser;

    partial void OnIsRightPaneFocusedChanged(bool value) =>
        OnPropertyChanged(nameof(FocusedBrowser));

    partial void OnActiveBrowserChanged(BrowserViewModel? value) =>
        OnPropertyChanged(nameof(FocusedBrowser));

    partial void OnSplitBrowserChanged(BrowserViewModel? value) =>
        OnPropertyChanged(nameof(FocusedBrowser));

    partial void OnIsSplitChanged(bool value) =>
        OnPropertyChanged(nameof(FocusedBrowser));

    /// <summary>Moves the focus to a pane, as clicking into it does in Chrome.</summary>
    public void FocusPane(bool right)
    {
        if (IsSplit)
        {
            IsRightPaneFocused = right;
        }
    }

    [ObservableProperty]
    private string _windowTitle = "Aphelion";

    /// <summary>True when this window has exactly one tab left.</summary>
    public bool IsSingleTab => _session.VisibleTabs.Count <= 1;

    /// <summary>The address of a tab, for handing to a new window when torn off.</summary>
    public static PageAddress? AddressOf(TabItemViewModel item) => item?.Tab.Address;

    /// <summary>Captures both pages represented by one visible tab entry.</summary>
    public TabTransferSnapshot CaptureTransfer(TabItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var partner = item.Tab.SplitPartnerId is { } partnerId
            ? _session.Tabs.FirstOrDefault(t => t.Id == partnerId)
            : null;

        return new TabTransferSnapshot(item.Tab.Address, partner?.Address);
    }

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

        // A pending split belongs to the tab that opened the picker. Switching
        // tabs cancels that pending operation instead of trapping navigation
        // behind an empty right pane.
        IsSplitPickerOpen = false;
        SyncTabs();
    }

    /// <summary>
    /// Drops a dragged tab before <paramref name="before"/>, joining
    /// <paramref name="group"/> when one is given. The group comes from the view:
    /// whatever tab or chip the pointer is over decides membership, which is how
    /// Chrome lets a tab join a single-member group — a neighbour-based rule can
    /// never fire there.
    /// </summary>
    public void DropTab(TabItemViewModel item, TabItemViewModel? before, TabGroupId? group)
    {
        ArgumentNullException.ThrowIfNull(item);

        _session.MoveVisibleTabBefore(item.Id, before?.Id, group);
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

        // Collapsing the last visible run would leave the strip empty. Chrome
        // opens a fresh tab rather than refusing the collapse, which would look
        // like the chip simply not working.
        if (group.IsCollapsed && _session.VisibleTabs.All(IsHidden))
        {
            Attach(_session.OpenTab());
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

    /// <summary>
    /// Groups a tab, creating a group if it has none. Acts on the tab the menu was
    /// opened on, falling back to the active one for the toolbar button — a menu
    /// opened on one tab must not group another.
    /// </summary>
    [RelayCommand]
    private void GroupActiveTab(TabItemViewModel? item)
    {
        if ((item?.Tab ?? _session.ActiveTab) is not { } tab)
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
        if (item is null ||
            item.IsSplit ||
            _session.ActiveTab is not { } active ||
            active.Id == item.Id)
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
            IsSplitPickerOpen = false;
            _session.Unsplit(active.Id);
            SyncTabs();
            return;
        }

        // Chrome opens the split first and asks what fills the second pane
        // afterwards, with the picker sitting in that empty pane. The picker's
        // "New tab" row covers the case where there is nothing to choose.
        var candidates = _session.VisibleTabs
            .Where(t => t.Id != active.Id && t.SplitPartnerId is null && !IsHidden(t))
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
    /// Told by the strip how much room it has. Whatever does not fit is listed in
    /// the overflow panel instead of being opened where the user cannot reach it.
    /// </summary>
    public void ReportStripRoom(double room)
    {
        if (room <= 0 || Math.Abs(room - _stripRoom) < 1)
        {
            return;
        }

        _stripRoom = room;
        SyncTabs();
    }

    /// <summary>
    /// How many of <paramref name="listed"/> fit the strip, taking each tab at its
    /// narrowest and allowing for the group chips that precede each run.
    /// </summary>
    /// <remarks>
    /// Greedy from the left, and deliberately so: whether a tab fits depends only
    /// on the tabs before it, all of which are shown. That makes the answer stable.
    /// A rule that weighed the whole set — the previous one measured the strip's
    /// current children — can change its mind about a tab because of a decision it
    /// made about that same tab, and then oscillate forever.
    /// </remarks>
    private int FitCount(List<BrowserTab> listed)
    {
        if (double.IsInfinity(_stripRoom))
        {
            return listed.Count;
        }

        var budget = _stripRoom;

        // A collapsed group is nothing but its chip, which still takes room.
        foreach (var group in _session.Groups)
        {
            if (group.IsCollapsed && _session.TabsInGroup(group.Id).Count > 0)
            {
                budget -= TabStripMetrics.ChipCostFor(group.Name);
            }
        }

        var count = 0;
        TabGroupId? run = null;

        foreach (var tab in listed)
        {
            var cost = tab.SplitPartnerId is not null
                ? TabStripMetrics.MinSplitTabWidth
                : TabStripMetrics.MinTabWidth;

            if (tab.GroupId is { } groupId && groupId != run)
            {
                cost += TabStripMetrics.ChipCostFor(_session.FindGroup(groupId)?.Name);
            }

            run = tab.GroupId;

            // At least one tab is always shown, however narrow the window.
            if (budget < cost && count > 0)
            {
                break;
            }

            budget -= cost;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Closes one side of a split pair, leaving the other as an ordinary tab.
    /// </summary>
    /// <remarks>
    /// <see cref="BrowsingSession.CloseTab"/> already breaks the pairing before
    /// removing the tab, so the survivor comes back as a normal tab on its own.
    /// </remarks>
    private void CloseSplitSide(TabItemViewModel? item, bool right)
    {
        var owner = item?.Tab ?? _session.ActiveTab;

        if (owner?.SplitPartnerId is not { } partnerId)
        {
            return;
        }

        var victim = right ? partnerId : owner.Id;

        Detach(victim);
        _session.CloseTab(victim);
        SyncTabs();
    }

    [RelayCommand]
    private void CloseLeftSide(TabItemViewModel? item) => CloseSplitSide(item, right: false);

    [RelayCommand]
    private void CloseRightSide(TabItemViewModel? item) => CloseSplitSide(item, right: true);

    /// <summary>
    /// Ends the split, keeping both pages as separate tabs. Acts on the tab the
    /// menu was opened on, falling back to the active one for the toolbar.
    /// </summary>
    [RelayCommand]
    private void CloseSplit(TabItemViewModel? item)
    {
        var owner = item?.Tab ?? _session.ActiveTab;

        if (owner is not null && _session.Unsplit(owner.Id))
        {
            SyncTabs();
        }
    }

    /// <summary>
    /// Moves a tab into a window of its own, as dragging it off the strip does.
    /// </summary>
    /// <remarks>
    /// Supplied by the window rather than done here: opening a window needs the
    /// window manager and the current geometry, neither of which a view model may
    /// know about. A window with one tab already is its own window, so the command
    /// is disabled there rather than closing this one and opening its twin.
    /// </remarks>
    public Action<TabItemViewModel>? MoveToNewWindow { get; set; }

    [RelayCommand]
    private void MoveTabToNewWindow(TabItemViewModel? item)
    {
        if (item is not null && !IsSingleTab)
        {
            MoveToNewWindow?.Invoke(item);
        }
    }

    /// <summary>
    /// Removes a tab because it moved to another window. Unlike closing, this never
    /// opens a replacement tab: the caller is responsible for where the tab went.
    /// </summary>
    public void DetachTab(TabItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var partnerId = item.Tab.SplitPartnerId;

        Detach(item.Id);

        if (partnerId is { } partner)
        {
            Detach(partner);
            _session.CloseTab(partner);
        }

        _session.CloseTab(item.Id);
        SyncTabs();
    }

    /// <summary>Adds a tab received from another window and activates it.</summary>
    public void AdoptTab(
        TabTransferSnapshot transfer,
        TabItemViewModel? before = null,
        TabGroupId? group = null)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        var tab = _session.OpenTab(transfer.PrimaryAddress);
        Attach(tab);
        _session.MoveVisibleTabBefore(tab.Id, before?.Id, group);
        _session.Activate(tab.Id);

        BrowserTab? partner = null;

        if (transfer.PartnerAddress is not null)
        {
            partner = _session.OpenTabNextTo(tab, transfer.PartnerAddress, activate: false);
            Attach(partner);
            _session.Split(tab.Id, partner.Id);
        }

        SyncTabs();

        if (transfer.PrimaryAddress is not null && _browsers.TryGetValue(tab.Id, out var browser))
        {
            browser.NavigateTo(transfer.PrimaryAddress);
        }

        if (partner is not null &&
            transfer.PartnerAddress is not null &&
            _browsers.TryGetValue(partner.Id, out var partnerBrowser))
        {
            partnerBrowser.NavigateTo(transfer.PartnerAddress);
        }
    }

    /// <summary>Navigates the active tab, used when a new window opens on an address.</summary>
    public void NavigateActiveTab(PageAddress address)
    {
        ActiveBrowser?.NavigateTo(address);
    }

    /// <summary>Reconstructs the second half of a transferred split entry.</summary>
    public void SplitActiveWithAddress(PageAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (_session.ActiveTab is not { } active)
        {
            return;
        }

        var partner = _session.OpenTabNextTo(active, address, activate: false);
        Attach(partner);
        _session.Split(active.Id, partner.Id);
        SyncTabs();
        _browsers[partner.Id].NavigateTo(address);
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
                group?.IsCollapsed ?? false,
                tab.SplitPartnerId is { } partnerId
                    ? _session.Tabs.ToList().FindIndex(t => t.Id == partnerId)
                    : null));
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
        var restored = new List<BrowserTab>();

        foreach (var saved in snapshot.Tabs)
        {
            PageAddress? address = null;

            if (saved.Address is not null &&
                Uri.TryCreate(saved.Address, UriKind.Absolute, out var uri))
            {
                PageAddress.TryCreate(uri, out address);
            }

            var tab = _session.OpenTab(address, activate: false);
            restored.Add(tab);
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

        for (var i = 0; i < snapshot.Tabs.Count; i++)
        {
            if (snapshot.Tabs[i].SplitPartnerIndex is { } partnerIndex &&
                partnerIndex >= 0 &&
                partnerIndex < restored.Count)
            {
                _session.Split(restored[i].Id, restored[partnerIndex].Id);
            }
        }

        var index = Math.Clamp(snapshot.ActiveIndex, 0, _session.Tabs.Count - 1);
        _session.Activate(restored[index].Id);
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
        // Two invariants, enforced here rather than at each call site so no
        // operation can leave the window unusable:
        //
        //   1. The session is never empty. A browser with no tabs shows nothing
        //      and offers no way back.
        //   2. At least one tab is visible. Every tab being inside collapsed
        //      groups would leave the strip blank.
        if (_session.IsEmpty)
        {
            Attach(_session.OpenTab());
        }
        else if (_session.VisibleTabs.Count > 0 && _session.VisibleTabs.All(IsHidden))
        {
            // Expand the group holding the active tab, or failing that the first
            // group there is.
            var reveal = _session.ActiveTab?.GroupId ?? _session.VisibleTabs[0].GroupId;

            if (reveal is { } groupId)
            {
                _session.FindGroup(groupId)?.Expand();
            }
        }

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
        var fits = FitCount(listed);
        var shown = listed.Take(fits).ToList();
        var overflowed = listed.Skip(fits).ToList();

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
            if (tab.IsSplitPartner)
            {
                continue;
            }

            // The chip is decided before the capacity filter: a collapsed group
            // shows nothing but its chip, so filtering on its hidden tabs first
            // would drop the group from the strip entirely.
            if (tab.GroupId is { } groupId)
            {
                var group = _session.FindGroup(groupId);

                if (group?.IsCollapsed == true)
                {
                    if (seenGroups.Add(groupId))
                    {
                        desired.Add(HeaderFor(groupId));
                    }

                    continue;
                }

                // A non-collapsed group whose members are all in Other Tabs must
                // not leave an orphan chip behind in the strip.
                if (!visibleIds.Contains(tab.Id))
                {
                    continue;
                }

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

            }
            else
            {
                run = null;
            }

            // Tabs beyond the strip's capacity live in the overflow panel.
            if (!visibleIds.Contains(tab.Id))
            {
                continue;
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

        // With no right pane there is nothing for it to hold.
        if (!IsSplit)
        {
            IsRightPaneFocused = false;
        }
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
            item = new TabItemViewModel(this, tab, _favicons);
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
