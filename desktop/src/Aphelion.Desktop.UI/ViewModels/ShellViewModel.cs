using System.Collections.ObjectModel;
using System.Globalization;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using Avalonia.Threading;
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
    private static readonly TimeSpan TabTransitionDuration = TimeSpan.FromMilliseconds(170);

    private readonly BrowsingSession _session = new();
    private readonly Func<BrowserViewModel> _browserFactory;
    private readonly Dictionary<TabId, BrowserViewModel> _browsers = [];
    private readonly Dictionary<TabId, TabItemViewModel> _tabItems = [];
    private readonly Dictionary<TabGroupId, GroupHeaderViewModel> _headers = [];
    private readonly ISessionStore? _sessionStore;
    private readonly IFaviconLoader? _favicons;
    private readonly ITabSoundPlayer? _tabSounds;
    private readonly HashSet<TabId> _closingTabs = [];
    private readonly HashSet<TabGroupId> _transitioningGroups = [];

    /// <summary>
    /// Groups that lost their last tab to ordinary tab closes, rather than being
    /// closed or deleted as a group. Their rows on the bar go with them: nothing
    /// was put away on purpose, so there is nothing to keep.
    /// </summary>
    private readonly HashSet<TabGroupId> _emptiedGroups = [];

    /// <summary>
    /// How much room the strip has, reported by the panel as it lays out. What
    /// does not fit spills into the overflow panel.
    /// </summary>
    private double _stripRoom = double.PositiveInfinity;

    /// <summary>The session as last written out, so an unchanged one is not rewritten.</summary>
    private string? _lastPersisted;

    public ShellViewModel(
        Func<BrowserViewModel> browserFactory,
        object? windowManager = null,
        ISessionStore? sessionStore = null,
        IFaviconLoader? favicons = null,
        ITabSoundPlayer? tabSounds = null,
        BookmarksViewModel? bookmarks = null,
        bool isPrivateWindow = false,
        DownloadsViewModel? downloads = null)
    {
        _browserFactory = browserFactory ?? throw new ArgumentNullException(nameof(browserFactory));
        WindowManager = windowManager;
        _sessionStore = sessionStore;
        _favicons = favicons;
        _tabSounds = tabSounds;
        Bookmarks = bookmarks;
        Downloads = downloads;
        IsPrivateWindow = isPrivateWindow;

        if (Bookmarks is not null)
        {
            // The bookmark view model is one per profile, shared by every window,
            // so it cannot hold a callback pointing at any single shell: each new
            // window would overwrite the last, and clicking the bar would act on
            // whichever window happened to open most recently rather than the one
            // in front of the user. Instead every shell registers itself, and the
            // bar asks which one is currently in front — see BookmarksViewModel.
            Bookmarks.Register(this);
            Bookmarks.BookmarksChanged += OnBookmarksChanged;
        }

        Overflow = new TabListViewModel(
            "Other tabs",
            item =>
            {
                ActivateTab(item);
                IsOverflowOpen = false;
            },
            close: item => CloseTabCommand.Execute(item),
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

    /// <summary>Single zoom notification shared by every tab in this window.</summary>
    public ZoomFeedbackViewModel ZoomFeedback { get; } = new();

    /// <summary>
    /// The saved bookmarks, shared with every other window in the profile. Null
    /// only in tests and previews, which construct a shell without them.
    /// </summary>
    public BookmarksViewModel? Bookmarks { get; }

    /// <summary>
    /// The download list, shared with every other window in the profile the
    /// same way bookmarks are. The toolbar button opens Chrome's bubble;
    /// Ctrl+J and the bubble's "Show all downloads" open the full page.
    /// </summary>
    public DownloadsViewModel? Downloads { get; }

    /// <summary>The toolbar flyout. Separate from the full downloads page.</summary>
    [ObservableProperty]
    private bool _isDownloadsBubbleOpen;

    [RelayCommand]
    private void ToggleDownloadsBubble() => IsDownloadsBubbleOpen = !IsDownloadsBubbleOpen;

    [RelayCommand]
    private void CloseDownloadsBubble() => IsDownloadsBubbleOpen = false;

    [RelayCommand]
    private void OpenDownloadsPage()
    {
        IsDownloadsBubbleOpen = false;

        var existing = _session.Tabs.FirstOrDefault(tab =>
            tab.IsDownloadsPage && !tab.IsSplitPartner);

        if (existing is not null)
        {
            if (existing.GroupId is { } groupId &&
                _session.FindGroup(groupId) is { IsCollapsed: true } hidden)
            {
                RevealGroup(hidden);
            }

            _session.Activate(existing.Id);
            SyncTabs();
            return;
        }

        var tab = _session.OpenTab();
        _tabSounds?.PlayTabOpened();
        Attach(tab);
        _browsers[tab.Id].ShowDownloads();
        var item = ItemFor(tab);
        item.IsExiting = true;
        SyncTabs();
        RevealAfterRender(item);
    }

    /// <summary>Escape dismisses the bubble. The downloads tab is a real tab.</summary>
    [RelayCommand]
    private void DismissDownloads() => IsDownloadsBubbleOpen = false;

    /// <summary>
    /// Opens the bubble in the window whose page started the download, as
    /// Chrome surfaces its flyout when a download begins.
    /// </summary>
    private void OnBrowserDownloadStarted(object? sender, EventArgs e) =>
        IsDownloadsBubbleOpen = true;

    private void OnBrowserAcceleratorKeyPressed(object? sender, EngineAcceleratorKeyPressedEventArgs e) =>
        AcceleratorKeyPressed?.Invoke(this, e);

    /// <summary>
    /// Private windows may read explicit bookmarks, but their live tab groups
    /// must never leak browsing activity into persistent saved-group records.
    /// </summary>
    public bool IsPrivateWindow { get; }

    /// <summary>
    /// Whether this shell's window is the one in front. The shared bookmark bar
    /// uses it to tell which window a click on it belongs to.
    /// </summary>
    [ObservableProperty]
    private bool _isWindowActive;

    /// <summary>The page on screen, for the bar's "Bookmark this page".</summary>
    public BookmarkPageCandidate? CurrentPageCandidate() =>
        ActiveBrowser is { Address: { } address } browser
            ? new BookmarkPageCandidate(browser.PageTitle, address, browser.FaviconAddress)
            : null;

    private void OnBookmarksChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(nameof(IsActivePageBookmarked));

    /// <summary>Releases profile-wide bookmark subscriptions when this window closes.</summary>
    public void ReleaseBookmarks()
    {
        if (Bookmarks is null)
        {
            return;
        }

        Bookmarks.BookmarksChanged -= OnBookmarksChanged;
        Bookmarks.Unregister(this);
    }

    /// <summary>
    /// Whether the bookmark bar is showing. On by default and left that way:
    /// hiding it belongs in the settings store, which does not exist yet, and a
    /// toggle whose state is forgotten on every launch is worse than none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBookmarkBarShowing))]
    private bool _isBookmarkBarVisible = true;

    /// <summary>
    /// True while F11 or an HTML fullscreen element has taken the window. The
    /// title bar, toolbar and bookmark bar hide so the page can fill the screen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBookmarkBarShowing))]
    private bool _isBrowserFullScreen;

    /// <summary>The bookmark bar stays off while the window is in fullscreen.</summary>
    public bool IsBookmarkBarShowing => IsBookmarkBarVisible && !IsBrowserFullScreen;

    /// <summary>
    /// Raised when any tab's HTML fullscreen element appears or disappears, so
    /// the window can enter or leave fullscreen around it.
    /// </summary>
    public event EventHandler? HtmlFullScreenElementChanged;

    /// <summary>
    /// Raised for F11 and Escape while a page in this window has native focus.
    /// Handled synchronously so WebView2 can be told not to swallow the key.
    /// </summary>
    public event EventHandler<EngineAcceleratorKeyPressedEventArgs>? AcceleratorKeyPressed;

    public bool HasHtmlFullScreenElement
    {
        get
        {
            foreach (var browser in _browsers.Values)
            {
                if (browser.ContainsFullScreenElement)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The add/edit form behind the star, or null when it is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBookmarkEditorOpen))]
    private BookmarkEditorViewModel? _bookmarkEditor;

    public bool IsBookmarkEditorOpen => BookmarkEditor is not null;

    /// <summary>Whether the page on screen is already saved, drawn as a filled star.</summary>
    public bool IsActivePageBookmarked =>
        Bookmarks?.IsBookmarked(ActiveBrowser?.Address) == true;

    [RelayCommand]
    private void ToggleBookmarkBar() => IsBookmarkBarVisible = !IsBookmarkBarVisible;

    /// <summary>
    /// Saves the open page, or reopens the form for it when it is already saved.
    /// </summary>
    /// <remarks>
    /// Clicking the star a second time edits rather than adding a duplicate,
    /// which is how Chrome's star behaves; the form's Remove button is the way
    /// back out.
    /// </remarks>
    [RelayCommand]
    private void BookmarkActiveTab()
    {
        if (Bookmarks is null || ActiveBrowser is not { Address: { } address } browser)
        {
            return;
        }

        var existing = Bookmarks.FindByAddress(address);

        if (existing is null)
        {
            // Saved before the form opens, so the star fills immediately and
            // dismissing the form leaves the bookmark in place.
            existing = Bookmarks.AddBookmark(
                Bookmarks.Tree.Root,
                browser.PageTitle,
                address,
                browser.FaviconAddress);
        }

        var editor = new BookmarkEditorViewModel(
            Bookmarks,
            address,
            existing.Name,
            browser.FaviconAddress,
            existing);

        editor.Closed += (_, _) => BookmarkEditor = null;
        BookmarkEditor = editor;
    }

    [RelayCommand]
    private void CloseBookmarkEditor() => BookmarkEditor = null;

    /// <summary>
    /// Keeps the bookmark bar's saved groups in step with the live ones: a group
    /// appears there as soon as it exists, follows its name, colour and pages,
    /// and stays after its tabs are closed.
    /// </summary>
    /// <remarks>
    /// Mirrored rather than saved on demand. A tab group is a working set the
    /// user has already assembled deliberately; requiring a second, explicit
    /// save before closing it is the step everyone forgets, and forgetting it
    /// loses the set. Deleting the row on the bar is what discards it — closing
    /// the tabs only puts it away.
    ///
    /// The link runs one way, from session to bar. Edits made on the bar are not
    /// pushed back into the live group: the folder is a record of the group, and
    /// a record that rewrites what it describes would be surprising.
    /// </remarks>
    private void SyncSavedGroups()
    {
        if (Bookmarks is null || _isReopeningGroup)
        {
            return;
        }

        if (IsPrivateWindow)
        {
            foreach (var id in _emptiedGroups)
            {
                Bookmarks.DiscardGroup(this, id.ToString(), removeSavedRowWhenUnlinked: false);
            }

            _emptiedGroups.Clear();
            return;
        }

        foreach (var group in _session.Groups)
        {
            MirrorSavedGroup(group);
        }

        // Deliberately not removing rows for groups that have left the session.
        // Closing a group's tabs is how it is put away, and its row on the bar is
        // what it is put away into; deleting is a separate act, and the only one
        // that discards the record — see DeleteGroup.
        //
        // The exception is a group whose last tab was closed one at a time rather
        // than through Close group. That empties the group without any intent to
        // keep it, so the row goes with it.
        foreach (var id in _emptiedGroups)
        {
            Bookmarks.DiscardGroup(this, id.ToString());
        }

        _emptiedGroups.Clear();
    }

    /// <summary>Captures one live group into its durable bookmark-bar record.</summary>
    private void MirrorSavedGroup(TabGroup group, bool replaceWithEmpty = false)
    {
        if (Bookmarks is null || IsPrivateWindow)
        {
            return;
        }

        var wasLegacyGroup = group.SavedGroupId is null;
        var proposedSavedGroupId = group.SavedGroupId ?? SavedTabGroupId.New();

        // A blank tab is recorded as a New Tab slot rather than dropped: it has
        // no address, but it is still one of the group's tabs, and skipping it
        // meant a group came back a tab short of how it was closed.
        var pages = _session.TabsInGroup(group.Id)
            .Select(tab => new BookmarkPageCandidate(
                tab.DisplayTitle,
                tab.Address,
                tab.FaviconAddress,
                HasResolvedTitle: !string.IsNullOrWhiteSpace(tab.Title),
                HasResolvedFavicon: tab.FaviconAddress is not null))
            .ToList();

        var adoptedSavedGroupId = Bookmarks.MirrorGroup(
            this,
            group.Id.ToString(),
            proposedSavedGroupId,
            group.Name,
            group.Color,
            pages,
            replaceWithEmpty,
            canAdoptIdentifiedExactMatch: wasLegacyGroup);

        group.LinkSavedGroup(adoptedSavedGroupId);
    }

    /// <summary>
    /// Opens bookmarked pages where the menu asked for them. Windows and panes
    /// are the shell's to arrange, which is why the bar delegates this back here.
    /// </summary>
    public void OpenBookmarkPages(IReadOnlyList<PageAddress> pages, BookmarkOpenTarget target)
    {
        if (pages.Count == 0)
        {
            return;
        }

        switch (target)
        {
            case BookmarkOpenTarget.CurrentTab:
                ActiveBrowser?.NavigateTo(pages[0]);

                // "Open all" on a folder still means every page: the first
                // replaces what is on screen, the rest arrive beside it.
                foreach (var page in pages.Skip(1))
                {
                    OpenInNewTab(page, activate: false);
                }

                break;

            case BookmarkOpenTarget.NewTab:
                for (var i = 0; i < pages.Count; i++)
                {
                    OpenInNewTab(pages[i], activate: i == 0);
                }

                break;

            case BookmarkOpenTarget.NewWindow when WindowManager is WindowManager manager:
            {
                var window = manager.CreateWindow(pages[0]);
                window.Show();

                if (window.DataContext is MainWindowViewModel { Shell: { } opened })
                {
                    foreach (var page in pages.Skip(1))
                    {
                        opened.OpenInNewTab(page, activate: false);
                    }
                }

                break;
            }

            case BookmarkOpenTarget.PrivateWindow when WindowManager is WindowManager privateManager:
            {
                var window = privateManager.CreatePrivateWindow();
                window.Show();

                if (window.DataContext is MainWindowViewModel { Shell: { } opened })
                {
                    for (var i = 0; i < pages.Count; i++)
                    {
                        if (i == 0)
                        {
                            opened.ActiveBrowser?.NavigateTo(pages[i]);
                        }
                        else
                        {
                            opened.OpenInNewTab(pages[i], activate: false);
                        }
                    }
                }

                break;
            }

            case BookmarkOpenTarget.SplitPane:
                OpenInSplit(pages[0]);
                break;
        }
    }

    /// <summary>Opens one page in a tab of its own.</summary>
    public void OpenInNewTab(PageAddress address, bool activate = true)
    {
        var tab = _session.OpenTab(activate: activate);
        Attach(tab);
        SyncTabs();
        _browsers[tab.Id].NavigateTo(address);
    }

    /// <summary>Opens a page beside the current one, in the second pane.</summary>
    private void OpenInSplit(PageAddress address)
    {
        if (_session.ActiveTab is not { SplitPartnerId: null } active)
        {
            return;
        }

        var tab = _session.OpenTab(activate: false);
        Attach(tab);

        if (!_session.Split(active.Id, tab.Id))
        {
            Detach(tab.Id);
            _session.CloseTab(tab.Id);
            return;
        }

        _browsers[tab.Id].NavigateTo(address);
        SyncTabs();
    }

    /// <summary>
    /// Reopens a saved group: its pages as tabs, gathered under a live group of
    /// the same name and colour.
    /// </summary>
    public void OpenSavedGroup(BookmarkFolder folder, BookmarkOpenTarget target)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (target == BookmarkOpenTarget.NewWindow && WindowManager is WindowManager manager)
        {
            var window = manager.CreateWindow();

            if (window.DataContext is MainWindowViewModel { Shell: { } opened })
            {
                Bookmarks?.ClaimSavedGroup(opened, folder.Id);
                opened.OpenSavedGroup(folder, BookmarkOpenTarget.CurrentTab);
            }

            window.Show();
            return;
        }

        if (target != BookmarkOpenTarget.CurrentTab)
        {
            return;
        }

        // Normal open focuses the one live occurrence, even when it belongs to
        // another window. The explicit "new window" path above is what moves it.
        if (FocusLiveGroupFor(folder.Id))
        {
            return;
        }

        if (Bookmarks?.LiveOwnerOf(this, folder.Id) is { } owner &&
            owner.FocusLiveGroupFor(folder.Id))
        {
            if (WindowManager is WindowManager ownerWindowManager)
            {
                ownerWindowManager.ActivateShell(owner);
            }

            return;
        }

        // One saved group has one live working set. Opening it from another
        // window first snapshots and closes the previous occurrence, then reads
        // that latest durable snapshot below.
        Bookmarks?.ClaimSavedGroup(this, folder.Id);

        // Read before anything is opened. The addresses are about to be handed
        // to tabs that mirror straight back into this same folder, so working
        // from a snapshot keeps the source from shifting underfoot.
        // New Tab slots come back as blank tabs, so the group reopens at the size
        // it was closed and in the same order.
        var pages = BookmarkTree.PagesOf(folder)
            .Select(BookmarkTree.AddressOf)
            .ToList();

        // Older builds could persist a group before its first page had an
        // address, producing a clickable row that silently did nothing forever.
        // Its lost URL cannot be reconstructed, but its group identity can: open
        // one blank member so the action is never a no-op and the row can recover.
        if (pages.Count == 0)
        {
            pages.Add(null);
        }

        var savedGroupId = Bookmarks is not null
            ? Bookmarks.EnsureSavedGroupId(folder)
            : folder.SavedGroupId ?? SavedTabGroupId.New();
        var group = _session.CreateGroup(
            folder.Name,
            folder.GroupColor ?? NextGroupColor(),
            savedGroupId);

        // The reopened group takes over the folder it came from rather than
        // mirroring itself into a second one beside it.
        Bookmarks?.AdoptGroupFolder(this, group.Id.ToString(), folder);

        // Suspended for the whole reopen: each new tab is blank until it
        // navigates, and mirroring in that state would empty the folder being
        // read from and leave the group recorded as having no pages at all.
        _isReopeningGroup = true;

        try
        {
            BrowserTab? firstOpened = null;
            var reusableBlank = _session.Tabs.Count == 1 &&
                _session.ActiveTab is { IsBlank: true, GroupId: null, SplitPartnerId: null } existingBlank
                    ? existingBlank
                    : null;

            for (var index = 0; index < pages.Count; index++)
            {
                var address = pages[index];
                var tab = index == 0 && reusableBlank is not null
                    ? reusableBlank
                    : _session.OpenTab(address, activate: false);

                _session.AddToGroup(tab.Id, group.Id);

                if (!ReferenceEquals(tab, reusableBlank))
                {
                    Attach(tab);
                }
                else if (address is not null)
                {
                    _browsers[tab.Id].NavigateTo(address);
                }

                firstOpened ??= tab;
            }

            if (firstOpened is not null)
            {
                _session.Activate(firstOpened.Id);
            }
        }
        finally
        {
            _isReopeningGroup = false;
        }

        SyncTabs();
    }

    /// <summary>
    /// True while a saved group is being reopened, when the tabs exist but have
    /// not navigated yet. See the remarks in <see cref="OpenSavedGroup"/>.
    /// </summary>
    private bool _isReopeningGroup;

    /// <summary>
    /// Closes the tabs of the group a bar row records, when that group is open
    /// in this window. Deleting a row the user is currently looking at should
    /// take its tabs with it.
    /// </summary>
    public void CloseLiveGroupFor(BookmarkNodeId rowId, bool preserveSavedGroup = false)
    {
        if (Bookmarks?.LiveGroupIdFor(this, rowId) is not { } sessionId)
        {
            return;
        }

        var group = _session.Groups.FirstOrDefault(g => g.Id.ToString() == sessionId);

        if (group is not null)
        {
            CloseGroupTabs(group.Id, preserveSavedGroup);
        }
    }

    public bool HasLiveGroupFor(BookmarkNodeId rowId)
    {
        if (Bookmarks?.LiveGroupIdFor(this, rowId) is not { } sessionId ||
            _session.Groups.FirstOrDefault(group => group.Id.ToString() == sessionId) is not { } group)
        {
            return false;
        }

        return _session.TabsInGroup(group.Id).Count > 0;
    }

    public bool FocusLiveGroupFor(BookmarkNodeId rowId)
    {
        if (Bookmarks?.LiveGroupIdFor(this, rowId) is not { } sessionId ||
            _session.Groups.FirstOrDefault(group => group.Id.ToString() == sessionId) is not { } group)
        {
            return false;
        }

        var tabs = _session.TabsInGroup(group.Id);

        if (tabs.Count == 0)
        {
            _session.CloseGroup(group.Id);
            return false;
        }

        RevealGroup(group);
        _session.Activate(tabs[0].Id);
        SyncTabs();
        return true;
    }

    /// <summary>
    /// Expands a group and releases the closed pose its members were left in.
    /// </summary>
    /// <remarks>
    /// Collapsing puts every member into the pose the transition ends on, and
    /// that pose is what keeps them drawn as gone. Expanding without releasing it
    /// puts the tabs back in the strip while leaving them invisible, which reads
    /// as a group that opened but lost its tabs. Only
    /// <see cref="ToggleGroupCollapsed"/> used to do this handshake, so every
    /// other way a group could expand had that bug.
    /// </remarks>
    private void RevealGroup(TabGroup group)
    {
        if (!group.IsCollapsed)
        {
            return;
        }

        var members = _session.VisibleTabs
            .Where(tab => tab.GroupId == group.Id)
            .Select(ItemFor)
            .ToArray();

        foreach (var member in members)
        {
            member.IsExiting = true;
        }

        group.Expand();

        if (members.Length > 0)
        {
            RevealAfterRender(members);
        }
    }

    /// <summary>Keeps a reopened live group aligned with its saved chip.</summary>
    public void RenameLiveGroupFor(BookmarkNodeId rowId, string name)
    {
        if (Bookmarks?.LiveGroupIdFor(this, rowId) is not { } sessionId ||
            _session.Groups.FirstOrDefault(group => group.Id.ToString() == sessionId) is not { } group)
        {
            return;
        }

        group.Rename(name);
        SyncTabs();
    }

    /// <summary>Keeps every live occurrence aligned with its saved colour.</summary>
    public void RecolorLiveGroupFor(BookmarkNodeId rowId, GroupColor color)
    {
        if (Bookmarks?.LiveGroupIdFor(this, rowId) is not { } sessionId ||
            _session.Groups.FirstOrDefault(group => group.Id.ToString() == sessionId) is not { } group)
        {
            return;
        }

        group.Recolor(color);
        SyncTabs();
    }

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

    /// <summary>
    /// Every open tab's browser, in the order the tabs sit in.
    /// </summary>
    /// <remarks>
    /// The window keeps a view alive for each of these for as long as the tab is
    /// open, and only changes which are visible. A web view is a native window: it
    /// is destroyed the moment it leaves the visual tree, taking the loaded page,
    /// the scroll position and any typed-in form with it. Showing the active tab
    /// through a single host meant every switch away and back reloaded the site
    /// from scratch.
    /// </remarks>
    public ObservableCollection<BrowserViewModel> Browsers { get; } = [];

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

    partial void OnActiveBrowserChanged(BrowserViewModel? value)
    {
        OnPropertyChanged(nameof(FocusedBrowser));

        // Switching tabs puts a different address on screen, which the star has
        // to reflect even though no navigation happened.
        OnPropertyChanged(nameof(IsActivePageBookmarked));
    }


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
        IsDownloadsBubbleOpen = false;
        var tab = _session.OpenTab();
        _tabSounds?.PlayTabOpened();
        Attach(tab);
        var item = ItemFor(tab);
        item.IsExiting = true;
        SyncTabs();
        RevealAfterRender(item);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task CloseTab(TabItemViewModel? item)
    {
        if (item is null ||
            !IsCurrent(item) ||
            item.Tab.GroupId is { } groupId && _transitioningGroups.Contains(groupId) ||
            !_closingTabs.Add(item.Id))
        {
            return;
        }

        try
        {
            if (HasRenderedSurface(item))
            {
                item.IsExiting = true;
                await Task.Delay(TabTransitionDuration).ConfigureAwait(true);
            }

            // Noted before the close, since the tab is the only thing that knows
            // which group it was in once it is gone.
            var leavingGroup = item.Tab.GroupId;

            // A delayed close may race a transfer or a group operation. Identity,
            // not merely the id, proves this is still the same tab in this window.
            if (!IsCurrent(item) || !_session.CloseTab(item.Id))
            {
                return;
            }

            // Closing tabs one by one until none are left is not putting a group
            // away — it is the group ceasing to exist, so its row does too.
            if (leavingGroup is { } emptied && _session.FindGroup(emptied) is null)
            {
                _emptiedGroups.Add(emptied);
            }

            _tabSounds?.PlayTabClosed();
            Detach(item.Id);

            TabItemViewModel? replacement = null;

            // Closing the last tab opens a fresh one rather than leaving an empty
            // window: a browser with no tabs has nothing to show and no way back.
            if (_session.IsEmpty)
            {
                var tab = _session.OpenTab();
                Attach(tab);
                replacement = ItemFor(tab);
                replacement.IsExiting = true;
            }

            SyncTabs();

            if (replacement is not null)
            {
                RevealAfterRender(replacement);
            }
        }
        finally
        {
            _closingTabs.Remove(item.Id);

            // When the operation was aborted, restore the still-live row. A
            // successfully removed view model has already been pruned.
            if (IsCurrent(item))
            {
                item.IsExiting = false;
            }
        }
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
        // behind an empty right pane. Leaving Downloads does the same: it is a
        // page, not a tab, so choosing a tab puts that page back on screen.
        IsSplitPickerOpen = false;
        IsDownloadsBubbleOpen = false;
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

        var previousGroup = item.Tab.GroupId;
        _session.MoveVisibleTabBefore(item.Id, before?.Id, group);
        DiscardGroupIfGone(previousGroup);
        SyncTabs();
    }

    /// <summary>Reorders an Other Tabs row without implicitly regrouping it.</summary>
    public void ReorderTab(TabItemViewModel item, TabItemViewModel? before)
    {
        ArgumentNullException.ThrowIfNull(item);

        _session.ReorderVisibleTabBefore(item.Id, before?.Id);
        SyncTabs();
    }

    /// <summary>Drops a dragged group so its run starts at <paramref name="targetIndex"/>.</summary>
    public void DropGroup(TabGroupId groupId, int targetIndex)
    {
        _session.MoveGroup(groupId, targetIndex);
        SyncTabs();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleGroupCollapsed(TabGroupId groupId)
    {
        if (!_transitioningGroups.Add(groupId))
        {
            return;
        }

        try
        {
            var group = _session.FindGroup(groupId);

            if (group is null)
            {
                return;
            }

            var members = _session.VisibleTabs
                .Where(tab => tab.GroupId == groupId)
                .Select(ItemFor)
                .ToArray();

            if (members.Any(item => _closingTabs.Contains(item.Id)))
            {
                return;
            }

            // Expansion inserts the members in their closed pose. Releasing the
            // pose on the render pass produces the reverse of the collapse.
            if (group.IsCollapsed)
            {
                foreach (var member in members)
                {
                    member.IsExiting = true;
                }

                group.Expand();
                SyncTabs();
                RevealAfterRender(members);
                return;
            }

            foreach (var member in members)
            {
                member.IsExiting = true;
            }

            if (members.Any(HasRenderedSurface))
            {
                await Task.Delay(TabTransitionDuration).ConfigureAwait(true);
            }

            // The group may have been removed by a transfer while the visual
            // transition was running. Never toggle a replacement by accident.
            group = _session.FindGroup(groupId);

            if (group is null || group.IsCollapsed)
            {
                foreach (var member in members)
                {
                    member.IsExiting = false;
                }

                return;
            }

            group.Collapse();

            TabItemViewModel? replacement = null;

            // Collapsing the last visible run would leave the strip empty. Chrome
            // opens a fresh tab rather than refusing the collapse.
            if (_session.VisibleTabs.All(IsHidden))
            {
                var tab = _session.OpenTab();
                Attach(tab);
                replacement = ItemFor(tab);
                replacement.IsExiting = true;
            }

            SyncTabs();

            // A tab moved out of the group during the delay remains visible and
            // must not inherit the group's hidden pose.
            foreach (var member in members.Where(member =>
                         !IsCurrent(member) || member.Tab.GroupId != groupId))
            {
                member.IsExiting = false;
            }

            if (replacement is not null)
            {
                RevealAfterRender(replacement);
            }
        }
        finally
        {
            _transitioningGroups.Remove(groupId);
        }
    }

    /// <summary>
    /// Closes a group's tabs, leaving its row on the bookmark bar. This is how a
    /// group is put away: the tabs go, the record of them stays, and clicking
    /// that row brings them back.
    /// </summary>
    [RelayCommand]
    private void CloseGroup(TabGroupId groupId) => CloseGroupTabs(groupId, preserveSavedGroup: true);

    private void CloseGroupTabs(TabGroupId groupId, bool preserveSavedGroup)
    {
        // Persist the exact final membership synchronously before any browser or
        // domain tab is detached. Relying on an earlier layout/property callback
        // is how closed groups ended up as permanently empty bookmark rows.
        if (preserveSavedGroup && _session.FindGroup(groupId) is { } closingGroup)
        {
            MirrorSavedGroup(closingGroup, replaceWithEmpty: true);
        }

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

    /// <summary>
    /// Closes a group's tabs and removes its saved row for good — the deliberate
    /// discard that <see cref="CloseGroup"/> is not.
    /// </summary>
    [RelayCommand]
    private void DeleteGroup(TabGroupId groupId)
    {
        if (Bookmarks?.DeleteLiveGroup(this, groupId.ToString()) == true)
        {
            return;
        }

        CloseGroupTabs(groupId, preserveSavedGroup: false);
    }

    [RelayCommand]
    private void UngroupTab(TabItemViewModel? item)
    {
        if (item?.Tab.GroupId is not { } groupId)
        {
            return;
        }

        _session.RemoveFromGroup(item.Id);
        DiscardGroupIfGone(groupId);

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
            var groupId = tab.GroupId.Value;
            _session.RemoveFromGroup(tab.Id);
            DiscardGroupIfGone(groupId);
        }
        else
        {
            var color = NextGroupColor();
            var group = _session.CreateGroup(
                $"Group {_session.Groups.Count + 1}",
                color,
                SavedTabGroupId.New());
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

        var previousGroup = item.Tab.GroupId;
        _session.Split(active.Id, item.Id);
        DiscardGroupIfGone(previousGroup);
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
    /// Atomically validates and removes a visible entry for transfer. A stale row
    /// cannot be extracted twice, so a repeated routed event cannot clone a tab in
    /// the destination window.
    /// </summary>
    public bool TryExtractTransfer(
        TabItemViewModel item,
        out TabTransferSnapshot? transfer,
        out bool sourceIsEmpty)
    {
        ArgumentNullException.ThrowIfNull(item);

        transfer = null;
        sourceIsEmpty = false;

        if (!_tabItems.TryGetValue(item.Id, out var current) ||
            !ReferenceEquals(current, item) ||
            !_session.VisibleTabs.Any(tab => ReferenceEquals(tab, item.Tab)))
        {
            return false;
        }

        var partnerId = item.Tab.SplitPartnerId;
        var previousGroup = item.Tab.GroupId;
        var partner = partnerId is { } id
            ? _session.Tabs.FirstOrDefault(tab => tab.Id == id)
            : null;

        transfer = new TabTransferSnapshot(
            item.Tab.Address,
            partner?.Address,
            item.Tab.IsDownloadsPage,
            IsPrivateWindow);

        Detach(item.Id);

        if (partnerId is { } partnerTabId)
        {
            Detach(partnerTabId);
            _session.CloseTab(partnerTabId);
        }

        if (!_session.CloseTab(item.Id))
        {
            transfer = null;
            return false;
        }

        DiscardGroupIfGone(previousGroup);

        if (_session.IsEmpty)
        {
            sourceIsEmpty = true;
            return true;
        }

        SyncTabs();
        return true;
    }

    private void DiscardGroupIfGone(TabGroupId? previousGroup)
    {
        if (previousGroup is not { } id || _session.FindGroup(id) is not null)
        {
            return;
        }

        Bookmarks?.DiscardGroup(
            this,
            id.ToString(),
            removeSavedRowWhenUnlinked: !IsPrivateWindow);
    }

    /// <summary>Adds a tab received from another window and activates it.</summary>
    public void AdoptTab(
        TabTransferSnapshot transfer,
        TabItemViewModel? before = null,
        TabGroupId? group = null)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        var tab = _session.OpenTab(transfer.IsDownloadsPage ? null : transfer.PrimaryAddress);
        Attach(tab);
        _session.MoveVisibleTabBefore(tab.Id, before?.Id, group);
        _session.Activate(tab.Id);

        if (transfer.IsDownloadsPage)
        {
            _browsers[tab.Id].ShowDownloads();
        }

        if (transfer.PartnerAddress is not null)
        {
            var partner = _session.OpenTabNextTo(tab, transfer.PartnerAddress, activate: false);
            Attach(partner);
            _session.Split(tab.Id, partner.Id);
        }

        SyncTabs();
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
                tab.IsDownloadsPage ? InternalPages.DownloadsAddress : tab.Address?.ToString(),
                group?.Name,
                group?.Color.ToString(),
                group?.IsCollapsed ?? false,
                tab.SplitPartnerId is { } partnerId
                    ? _session.Tabs.ToList().FindIndex(t => t.Id == partnerId)
                    : null,
                group?.SavedGroupId?.ToString()));
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

        if (!snapshot.Tabs.Any(saved => saved is not null))
        {
            return false;
        }

        var restoredDurableIds = new HashSet<SavedTabGroupId>();
        TabGroup? currentRun = null;
        string? currentRunSignature = null;
        var restored = new Dictionary<int, BrowserTab>();

        // Navigating each restored tab raises Address immediately. Suppress group
        // mirroring until every member has been reconstructed, otherwise the first
        // member creates a partial duplicate before the complete saved row can be
        // matched and adopted.
        _isReopeningGroup = true;

        try
        {
            for (var sourceIndex = 0; sourceIndex < snapshot.Tabs.Count; sourceIndex++)
            {
                var saved = snapshot.Tabs[sourceIndex];

                if (saved is null)
                {
                    currentRun = null;
                    currentRunSignature = null;
                    continue;
                }

                PageAddress? address = null;
                var isDownloadsPage = InternalPages.IsDownloads(saved.Address);

                if (!isDownloadsPage &&
                    saved.Address is not null &&
                    Uri.TryCreate(saved.Address, UriKind.Absolute, out var uri))
                {
                    PageAddress.TryCreate(uri, out address);
                }

                var tab = _session.OpenTab(address, activate: false);
                restored[sourceIndex] = tab;
                Attach(tab);

                if (isDownloadsPage)
                {
                    _browsers[tab.Id].ShowDownloads();
                }

                if (saved.GroupName is not null)
                {
                    var parsedSavedGroupId = TryParseSavedGroupId(saved.SavedGroupId);
                    var signature =
                        $"{saved.GroupName}|{saved.GroupColor}|{saved.GroupCollapsed}|{parsedSavedGroupId}";

                    // Group membership is a contiguous run in BrowsingSession.
                    // Reuse an id only inside that run: a corrupt snapshot can
                    // repeat the same durable id later for a different group,
                    // and globally keying by id silently merged their pages.
                    if (currentRun is null || currentRunSignature != signature)
                    {
                        var acceptedId = parsedSavedGroupId is { } durableId &&
                            restoredDurableIds.Add(durableId)
                                ? durableId
                                : (SavedTabGroupId?)null;
                        currentRun = CreateRestoredGroup(saved, acceptedId);
                        currentRunSignature = signature;
                    }

                    var group = currentRun;

                    _session.AddToGroup(tab.Id, group.Id);

                    if (saved.GroupCollapsed)
                    {
                        group.Collapse();
                    }
                }
                else
                {
                    currentRun = null;
                    currentRunSignature = null;
                }

                // Marks the tab as loading so the navigation replays once its view
                // attaches an engine session.
                if (address is not null)
                {
                    _browsers[tab.Id].NavigateTo(address);
                }
            }

            foreach (var (sourceIndex, tab) in restored)
            {
                if (snapshot.Tabs[sourceIndex]?.SplitPartnerIndex is { } partnerIndex &&
                    restored.TryGetValue(partnerIndex, out var partner))
                {
                    _session.Split(tab.Id, partner.Id);
                }
            }

            var active = restored.TryGetValue(snapshot.ActiveIndex, out var selected)
                ? selected
                : restored.OrderBy(entry => entry.Key).First().Value;
            _session.Activate(active.Id);
        }
        finally
        {
            _isReopeningGroup = false;
        }

        SyncTabs();
        return true;

        TabGroup CreateRestoredGroup(SessionTabSnapshot saved, SavedTabGroupId? savedGroupId)
        {
            var color = Enum.TryParse<GroupColor>(saved.GroupColor, out var parsed) &&
                Enum.IsDefined(parsed)
                ? parsed
                : GroupColor.Slate;
            return _session.CreateGroup(saved.GroupName ?? "Group", color, savedGroupId);
        }
    }

    /// <summary>Cycles the palette so consecutive groups are visually distinct.</summary>
    private GroupColor NextGroupColor()
    {
        var palette = Enum.GetValues<GroupColor>();
        return palette[_session.Groups.Count % palette.Length];
    }

    private static SavedTabGroupId? TryParseSavedGroupId(string? value) =>
        SavedTabGroupId.TryParse(value, out var id) ? id : null;

    private void Attach(BrowserTab tab)
    {
        var browser = _browserFactory();
        browser.Bind(tab);
        browser.PropertyChanged += OnBrowserPropertyChanged;
        browser.ZoomFeedbackRequested += OnZoomFeedbackRequested;
        browser.DownloadStarted += OnBrowserDownloadStarted;
        browser.AcceleratorKeyPressed += OnBrowserAcceleratorKeyPressed;
        _browsers[tab.Id] = browser;
    }

    private void Detach(TabId id)
    {
        if (_browsers.Remove(id, out var browser))
        {
            browser.PropertyChanged -= OnBrowserPropertyChanged;
            browser.ZoomFeedbackRequested -= OnZoomFeedbackRequested;
            browser.DownloadStarted -= OnBrowserDownloadStarted;
            browser.AcceleratorKeyPressed -= OnBrowserAcceleratorKeyPressed;

            if (browser.ContainsFullScreenElement)
            {
                HtmlFullScreenElementChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Brings <see cref="Browsers"/> in line with the open tabs, reconciling in
    /// place so a browser that is merely moving position keeps its view — and
    /// therefore its loaded page — rather than being dropped and rebuilt.
    /// </summary>
    private void SyncBrowsers()
    {
        var desired = new List<BrowserViewModel>();

        foreach (var tab in _session.Tabs)
        {
            if (_browsers.TryGetValue(tab.Id, out var browser))
            {
                desired.Add(browser);
            }
        }

        for (var i = Browsers.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(Browsers[i]))
            {
                Browsers.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var index = Browsers.IndexOf(desired[i]);

            if (index < 0)
            {
                Browsers.Insert(i, desired[i]);
            }
            else if (index != i)
            {
                Browsers.Move(index, i);
            }
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

        // Navigating changes whether the star is filled, so it follows the page
        // rather than only refreshing when the bookmarks themselves change.
        if (e.PropertyName == nameof(BrowserViewModel.Address))
        {
            OnPropertyChanged(nameof(IsActivePageBookmarked));
        }

        // A grouped tab's recorded page follows what it actually shows, so the
        // saved group reopens where the tabs were left rather than where they
        // started.
        if (e.PropertyName is nameof(BrowserViewModel.Address)
            or nameof(BrowserViewModel.PageTitle)
            or nameof(BrowserViewModel.FaviconAddress))
        {
            SyncSavedGroups();
        }

        if (e.PropertyName == nameof(BrowserViewModel.ContainsFullScreenElement))
        {
            HtmlFullScreenElementChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnZoomFeedbackRequested(object? sender, ZoomFeedbackRequestedEventArgs e) =>
        ZoomFeedback.Show(e.Percent);

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

            if (reveal is { } groupId && _session.FindGroup(groupId) is { } hidden)
            {
                // Closing the last loose tab while a collapsed group holds the
                // rest lands here. The group has to come back drawn, not merely
                // expanded, or the strip is left showing a chip and nothing else.
                RevealGroup(hidden);
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
        var listed = _session.VisibleTabs
            .Where(tab => !IsHidden(tab))
            .DistinctBy(tab => tab.Id)
            .ToList();
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

        // A reconcile must also repair duplicate occurrences seeded by a stale
        // visual event from an older build. Contains/IndexOf alone would preserve
        // the extra copy forever because both occurrences are still "desired".
        var existingTabs = new HashSet<TabId>();
        var existingGroups = new HashSet<TabGroupId>();

        for (var i = 0; i < StripItems.Count;)
        {
            var isFirst = StripItems[i] switch
            {
                TabItemViewModel tab => existingTabs.Add(tab.Id),
                GroupHeaderViewModel group => existingGroups.Add(group.Id),
                _ => true,
            };

            if (isFirst)
            {
                i++;
            }
            else
            {
                StripItems.RemoveAt(i);
            }
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
        SyncBrowsers();

        // Groups are recorded on the bar as they change, so closing their tabs
        // leaves the record behind rather than losing the set.
        SyncSavedGroups();

        // Which groups are live has just been settled, so the bar can redraw the
        // outline that tells a live saved group from a merely stored one.
        Bookmarks?.RefreshLiveGroupState();
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

    /// <summary>
    /// Writes the session out whenever it has actually changed.
    /// </summary>
    /// <remarks>
    /// It used to be saved once, from the application's shutdown handler. Anything
    /// that ended the process without raising that event — a crash, a kill, or a
    /// close that the lifetime turns straight into a shutdown — lost every tab
    /// opened since the last time it did fire, which is why a restart could come
    /// back with a session from days earlier or with nothing at all.
    /// <para>
    /// Saving on change instead means there is no moment whose loss costs
    /// anything. The comparison is what makes it cheap: this runs on every layout
    /// pass that resizes the strip, and only an actual difference reaches the
    /// disk.
    /// </para>
    /// </remarks>
    private void PersistSession()
    {
        if (_sessionStore is null || _isReopeningGroup)
        {
            return;
        }

        var snapshot = CaptureSnapshot();

        var key = string.Join(
            '\n',
            snapshot.Tabs
                .Select(t =>
                    $"{t.Address}|{t.GroupName}|{t.GroupColor}|{t.GroupCollapsed}|" +
                    $"{t.SplitPartnerIndex}|{t.SavedGroupId}")
                .Prepend(snapshot.ActiveIndex.ToString(CultureInfo.InvariantCulture)));

        if (key == _lastPersisted)
        {
            return;
        }

        _lastPersisted = key;
        _sessionStore.Save(snapshot);
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

        // Reached from both SyncTabs and a browser reporting a change, so a tab
        // opening, moving, closing or navigating all persist.
        PersistSession();
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
                if (Bookmarks?.SavedGroupRowIdFor(this, id.ToString()) is { } rowId)
                {
                    Bookmarks.Rename(rowId, name);
                }
                else
                {
                    _session.FindGroup(id)?.Rename(name);
                    SyncTabs();
                }
            },
            recolor: color =>
            {
                if (Bookmarks?.SavedGroupRowIdFor(this, id.ToString()) is { } rowId)
                {
                    Bookmarks.RecolorSavedGroup(rowId, color);
                }
                else
                {
                    _session.FindGroup(id)?.Recolor(color);
                    SyncTabs();
                }
            },
            toggle: () => ToggleGroupCollapsedCommand.Execute(id),
            ungroup: () =>
            {
                foreach (var tab in _session.TabsInGroup(id))
                {
                    _session.RemoveFromGroup(tab.Id);
                }

                DiscardGroupIfGone(id);
                SyncTabs();
            },
            close: () => CloseGroup(id),
            delete: () => DeleteGroup(id));

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

    /// <summary>Whether this exact row still belongs to this window.</summary>
    private bool IsCurrent(TabItemViewModel item) =>
        _tabItems.TryGetValue(item.Id, out var current) &&
        ReferenceEquals(current, item) &&
        _session.VisibleTabs.Any(tab => ReferenceEquals(tab, item.Tab));

    /// <summary>Whether delaying a mutation can produce an animation the user sees.</summary>
    private bool HasRenderedSurface(TabItemViewModel item) =>
        StripItems.Contains(item) ||
        IsOverflowOpen && Overflow.Page.Contains(item);

    /// <summary>
    /// Clears the closed pose after the new controls have entered the visual tree.
    /// Posting at render priority is essential: clearing it in the same layout
    /// pass would skip the transition entirely.
    /// </summary>
    private void RevealAfterRender(params TabItemViewModel[] items)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                foreach (var item in items)
                {
                    if (IsCurrent(item))
                    {
                        item.IsExiting = false;
                    }
                }
            },
            DispatcherPriority.Render);
    }
}
