using System.Collections.ObjectModel;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Application.UseCases;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// The bookmark bar: the rows it shows, and every command those rows and the
/// folder popups beneath them invoke.
/// </summary>
/// <remarks>
/// Commands live here rather than on each row for the same reason
/// <see cref="TabListViewModel"/> centralises its own: a row in a popup cannot
/// bind to an ancestor, so it reaches its owner directly, and one owner is
/// simpler to keep correct than one command set per row.
/// </remarks>
public sealed partial class BookmarksViewModel : ViewModelBase
{
    private readonly BookmarkTree _tree;
    private readonly IBookmarkStore _store;
    private readonly IFaviconLoader? _favicons;

    public BookmarksViewModel(
        BookmarkTree tree,
        IBookmarkStore store,
        IFaviconLoader? favicons = null)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _favicons = favicons;

        SyncBarItems();
    }

    /// <summary>
    /// The ordinary bookmarks and folders at the top level, shown to the right
    /// of the divider.
    /// </summary>
    public ObservableCollection<BookmarkNodeViewModel> BarItems { get; } = [];

    /// <summary>
    /// The saved tab groups, shown to the left of the divider. Split from
    /// <see cref="BarItems"/> for presentation only — both are children of the
    /// same root, and dragging moves rows between them.
    /// </summary>
    public ObservableCollection<BookmarkNodeViewModel> GroupItems { get; } = [];

    public bool HasBarItems => BarItems.Count > 0;

    public bool HasGroupItems => GroupItems.Count > 0;

    /// <summary>
    /// The divider only earns its place when it has something on both sides.
    /// With no saved groups it would otherwise lead the bar, separating the
    /// bookmarks from nothing at all.
    /// </summary>
    public bool ShowsDivider => HasGroupItems && HasBarItems;

    public bool HasAnyItems => GroupItems.Count > 0 || BarItems.Count > 0;

    /// <summary>
    /// Every row along the bar, groups and bookmarks alike, in the order the
    /// tree holds them. For the code that walks the bar rather than draws it.
    /// </summary>
    public IEnumerable<BookmarkNodeViewModel> TopLevelRows => GroupItems.Concat(BarItems);


    [RelayCommand]
    private void CreateGroup() => ActiveShell?.GroupActiveTabCommand.Execute(null);

    public BookmarkTree Tree => _tree;

    /// <summary>
    /// The windows showing this bar. Every action it offers — navigating,
    /// reopening a group, adding the current page — belongs to one window, but
    /// the bar itself is shared by all of them, so the shell to act on is looked
    /// up at the moment of the click rather than stored once.
    /// </summary>
    private readonly List<ShellViewModel> _shells = [];

    /// <summary>
    /// The last browser window that was genuinely activated. Native context menus
    /// can temporarily deactivate their owner while they are open, so consulting
    /// only <see cref="ShellViewModel.IsWindowActive"/> at command time can send a
    /// menu action to another window.
    /// </summary>
    private ShellViewModel? _lastActiveShell;

    /// <summary>Adds a window's shell to the set the bar can act on.</summary>
    public void Register(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        if (!_shells.Contains(shell))
        {
            _shells.Add(shell);
        }
    }

    public void MarkActive(ShellViewModel shell)
    {
        Register(shell);

        if (!ReferenceEquals(_lastActiveShell, shell))
        {
            // Folder and rename cards are transient window UI. Close the old
            // window's state before transferring command ownership so an open
            // native popup can never execute later against the new window.
            CloseAllFolders();
            RenameTarget = null;
        }

        _lastActiveShell = shell;
        OnPropertyChanged(nameof(ActionShell));
    }

    public void Unregister(ShellViewModel shell)
    {
        _shells.Remove(shell);
        _groupFolders.Remove(shell);

        // A closed window takes its live groups with it, and no surviving window
        // syncs on its own account, so the bar is redrawn here.
        RefreshLiveGroupState();

        if (ReferenceEquals(_lastActiveShell, shell))
        {
            CloseAllFolders();
            RenameTarget = null;
            _lastActiveShell = _shells.LastOrDefault();
            OnPropertyChanged(nameof(ActionShell));
        }
    }

    /// <summary>
    /// Which window a click on the bar acts on: the one that reports itself as
    /// active, falling back to the only one there is.
    /// </summary>
    /// <remarks>
    /// A bar is drawn once per window, but they share this view model, so a click
    /// arrives with no indication of where it came from. Asking the shells which
    /// of them is in front is the one thing that reliably distinguishes them.
    /// </remarks>
    private ShellViewModel? ActiveShell =>
        _shells.FirstOrDefault(shell => shell.IsWindowActive) ??
        (_lastActiveShell is not null && _shells.Contains(_lastActiveShell) ? _lastActiveShell : null) ??
        _shells.FirstOrDefault();

    /// <summary>The shell that owns commands and transient bar presentation.</summary>
    public ShellViewModel? ActionShell => ActiveShell;

    /// <summary>
    /// Whether transient bookmark-bar UI currently belongs to this window.
    /// The bookmark tree is profile-wide, but a folder/rename popup must appear
    /// only in the window where its gesture began.
    /// </summary>
    public bool IsActionShell(ShellViewModel shell) =>
        ReferenceEquals(ActiveShell, shell);

    /// <summary>Raised when the set of saved addresses changes, so the star can update.</summary>
    public event EventHandler? BookmarksChanged;

    /// <summary>
    /// Opens a bookmark, or toggles a folder's dropdown. One command for both
    /// because the bar and the popups bind the same row template to it.
    /// </summary>
    [RelayCommand]
    private void Open(BookmarkNodeViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.Node is Bookmark bookmark)
        {
            CloseAllFolders();
            ActiveShell?.NavigateActiveTab(bookmark.Address);
            return;
        }

        // A saved group reopens as a group rather than expanding into a menu:
        // its whole point is the set of pages, taken together.
        if (item.Node is BookmarkFolder { IsSavedGroup: true } saved)
        {
            CloseAllFolders();
            ActiveShell?.OpenSavedGroup(saved, BookmarkOpenTarget.CurrentTab);
            return;
        }

        // Only what is beside and beneath this folder closes; the folders it sits
        // inside stay open. Closing everything shut the parent dropdown too, so a
        // folder within a folder disappeared at the moment it was opened.
        var wasOpen = item.IsOpen;
        CloseSiblingsOf(item);
        item.IsOpen = !wasOpen;
    }

    /// <summary>
    /// Shows the branch ending at <paramref name="item"/>: opens it when it is a
    /// folder, and closes whatever else was showing beside it.
    /// </summary>
    /// <remarks>
    /// This is what hovering along an open menu calls. A plain bookmark opens
    /// nothing but still closes the branch that was showing, since moving onto
    /// one is a move away from the folder being browsed.
    /// </remarks>
    public void RevealFolder(BookmarkNodeViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        CloseSiblingsOf(item);
        item.IsOpen = item.CanExpandAsFolder;
    }

    /// <summary>
    /// Closes everything except <paramref name="item"/> and the folders it is
    /// nested inside — that is, its siblings at every level up to the bar, and
    /// anything already open beneath it.
    /// </summary>
    private void CloseSiblingsOf(BookmarkNodeViewModel item)
    {
        var ancestry = new HashSet<BookmarkNodeViewModel>();

        for (var walk = FindPathTo(item, TopLevelRows); walk is not null; walk = walk.Parent)
        {
            ancestry.Add(walk.Row);
        }

        foreach (var row in TopLevelRows)
        {
            CloseExcept(row, ancestry);
        }
    }

    private static void CloseExcept(BookmarkNodeViewModel row, HashSet<BookmarkNodeViewModel> keep)
    {
        if (!keep.Contains(row))
        {
            CloseFolders(row);
            return;
        }

        if (row.Children is null)
        {
            return;
        }

        foreach (var child in row.Children)
        {
            CloseExcept(child, keep);
        }
    }

    /// <summary>A row and the chain of folders it sits inside, nearest first.</summary>
    private sealed record RowPath(BookmarkNodeViewModel Row, RowPath? Parent);

    private static RowPath? FindPathTo(
        BookmarkNodeViewModel target,
        IEnumerable<BookmarkNodeViewModel> rows,
        RowPath? parent = null)
    {
        foreach (var row in rows)
        {
            var path = new RowPath(row, parent);

            if (ReferenceEquals(row, target))
            {
                return path;
            }

            if (row.Children is { } children && FindPathTo(target, children, path) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    [RelayCommand]
    private void Delete(BookmarkNodeViewModel? item)
    {
        if (item is null || !_tree.RemoveNode(item.Id))
        {
            return;
        }

        CommitStructuralChange();
    }

    /// <summary>
    /// Opens a row somewhere other than the current tab. A folder contributes
    /// every page it holds, which is what "open all" means for one.
    /// </summary>
    [RelayCommand]
    private void OpenIn(BookmarkOpenRequest? request)
    {
        if (request?.Node is not { } node)
        {
            return;
        }

        if (node.Node is BookmarkFolder { IsSavedGroup: true } savedGroup)
        {
            if (request.Target is BookmarkOpenTarget.CurrentTab or BookmarkOpenTarget.NewWindow)
            {
                CloseAllFolders();
                ActiveShell?.OpenSavedGroup(savedGroup, request.Target);
            }

            return;
        }

        var pages = PagesOf(node);

        if (pages.Count == 0)
        {
            return;
        }

        CloseAllFolders();
        ActiveShell?.OpenBookmarkPages(pages, request.Target);
    }

    /// <summary>The addresses a row would open: one for a bookmark, all for a folder.</summary>
    private static List<PageAddress> PagesOf(BookmarkNodeViewModel node) => node.Node switch
    {
        Bookmark bookmark => [bookmark.Address],
        BookmarkFolder folder => BookmarkTree.Descendants(folder)
            .OfType<Bookmark>()
            .Select(b => b.Address)
            .ToList(),
        _ => [],
    };

    /// <summary>
    /// Takes a row for a later paste, and removes it. Cutting before the paste
    /// lands would lose it, so the node is held here rather than deleted now.
    /// </summary>
    [RelayCommand]
    private void Cut(BookmarkNodeViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _clipboard = Capture(item);
        _clipboardCutId = item.Id;
        OnPropertyChanged(nameof(CanPaste));
    }

    [RelayCommand]
    private void Copy(BookmarkNodeViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _clipboard = Capture(item);
        _clipboardCutId = null;
        OnPropertyChanged(nameof(CanPaste));
    }

    /// <summary>
    /// Puts what was cut or copied inside <paramref name="target"/> — or beside
    /// it, when the target is a bookmark rather than a folder.
    /// </summary>
    [RelayCommand]
    private void Paste(BookmarkNodeViewModel? target)
    {
        if (_clipboard is not { } payload)
        {
            return;
        }

        var (parent, before) = ResolvePasteSite(target);

        // Saved groups are maintained by their live tab group. Allowing an
        // ordinary bookmark edit to mutate that folder would only be overwritten
        // by the next navigation, and cutting a folder into itself (or one of its
        // descendants) would detach the entire subtree from the bookmark root.
        if (parent.IsSavedGroup ||
            _clipboardCutId is { } candidateCutId && IsSameOrBelow(candidateCutId, parent))
        {
            return;
        }

        // A cut moves the live node at paste time. Rebuilding the snapshot would
        // silently discard a rename or new child added after Cut was chosen.
        if (_clipboardCutId is { } cutId)
        {
            if (_tree.FindNode(cutId) is null)
            {
                _clipboard = null;
                _clipboardCutId = null;
                OnPropertyChanged(nameof(CanPaste));
                return;
            }

            if (!_tree.MoveNode(cutId, parent, before))
            {
                return;
            }

            _clipboard = null;
            _clipboardCutId = null;
            OnPropertyChanged(nameof(CanPaste));
            CommitStructuralChange();
            return;
        }

        BookmarkSnapshotMapper.RestoreNode(_tree, parent, payload, before);
        _clipboard = null;
        OnPropertyChanged(nameof(CanPaste));
        CommitStructuralChange();
    }

    private bool IsSameOrBelow(BookmarkNodeId ancestorId, BookmarkFolder candidate)
    {
        for (BookmarkFolder? walk = candidate; walk is not null; walk = _tree.ParentOf(walk.Id))
        {
            if (walk.Id == ancestorId)
            {
                return true;
            }
        }

        return false;
    }

    public bool CanPaste => _clipboard is not null;

    /// <summary>Saves the page on screen into a chosen folder.</summary>
    [RelayCommand]
    private void AddPageHere(BookmarkNodeViewModel? target)
    {
        // An addressless candidate is a New Tab, which is only ever recorded as a
        // slot inside a saved group — there is nothing for an ordinary bookmark
        // to point at.
        if (ActiveShell?.CurrentPageCandidate() is not { Address: { } address } page)
        {
            return;
        }

        var (parent, _) = ResolvePasteSite(target);

        if (parent.IsSavedGroup)
        {
            return;
        }

        var bookmark = _tree.AddBookmark(parent, page.Title, address, page.FaviconAddress);
        CommitStructuralChange();
        BeginRenameOf(bookmark.Id);
    }

    /// <summary>Where a paste or an add should land, given the row clicked on.</summary>
    private (BookmarkFolder Parent, BookmarkNodeId? Before) ResolvePasteSite(BookmarkNodeViewModel? target)
    {
        if (target is null)
        {
            return (_tree.Root, null);
        }

        // Into a folder, but beside a bookmark: dropping something inside a page
        // is not a thing the tree can express.
        if (_tree.FindFolder(target.Id) is { } folder)
        {
            return (folder, null);
        }

        return (_tree.ParentOf(target.Id) ?? _tree.Root, target.Id);
    }

    private BookmarkNodeSnapshot? _clipboard;
    private BookmarkNodeId? _clipboardCutId;

    private static BookmarkNodeSnapshot Capture(BookmarkNodeViewModel node) =>
        BookmarkSnapshotMapper.CaptureNode(node.Node);

    /// <summary>
    /// Opens the rename box for a row. Folders have no other way to be named —
    /// the star's form only covers bookmarks — so this is how a folder created
    /// from the context menu stops being called "New folder".
    /// </summary>
    [RelayCommand]
    private void BeginRename(BookmarkNodeViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        CloseAllFolders();
        RenameTarget = item;
        RenameText = item.Name;
    }

    [RelayCommand]
    private void CommitRename()
    {
        if (RenameTarget is { } target)
        {
            Rename(target.Id, RenameText);
        }

        RenameTarget = null;
    }

    [RelayCommand]
    private void CancelRename() => RenameTarget = null;

    /// <summary>The row being renamed, or null when the box is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenaming))]
    private BookmarkNodeViewModel? _renameTarget;

    public bool IsRenaming => RenameTarget is not null;

    [ObservableProperty]
    private string _renameText = string.Empty;

    /// <summary>
    /// Adds a folder at the end of the bar, and opens its name for editing —
    /// a folder called "New folder" is of no use until it is named.
    /// </summary>
    [RelayCommand]
    private void CreateFolder()
    {
        var folder = _tree.AddFolder(_tree.Root, "New folder");
        CommitStructuralChange();
        BeginRenameOf(folder.Id);
    }

    /// <summary>Adds a folder inside an open folder's dropdown.</summary>
    [RelayCommand]
    private void CreateFolderIn(BookmarkNodeViewModel? parent)
    {
        var destination = parent is null
            ? _tree.Root
            : ResolvePasteSite(parent).Parent;
        var folder = _tree.AddFolder(destination, "New folder");
        CommitStructuralChange();
        BeginRenameOf(folder.Id);
    }

    private void BeginRenameOf(BookmarkNodeId id)
    {
        if (FindRow(id, TopLevelRows) is { } row)
        {
            RenameTarget = row;
            RenameText = row.Name;
        }
    }

    private static BookmarkNodeViewModel? FindRow(
        BookmarkNodeId id,
        IEnumerable<BookmarkNodeViewModel> rows)
    {
        foreach (var row in rows)
        {
            if (row.Id == id)
            {
                return row;
            }

            if (row.Children is { } children && FindRow(id, children) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    public Bookmark AddBookmark(
        BookmarkFolder parent,
        string name,
        PageAddress address,
        PageAddress? faviconAddress = null)
    {
        var attached = _tree.FindFolder(parent.Id);
        var destination = ReferenceEquals(attached, parent) ? parent : _tree.Root;
        var bookmark = _tree.AddBookmark(destination, name, address, faviconAddress);
        CommitStructuralChange();
        return bookmark;
    }

    public BookmarkFolder AddFolder(BookmarkFolder parent, string name)
    {
        var attached = _tree.FindFolder(parent.Id);
        var destination = ReferenceEquals(attached, parent) && !parent.IsSavedGroup
            ? parent
            : _tree.Root;
        var folder = _tree.AddFolder(destination, name);
        CommitStructuralChange();
        return folder;
    }

    /// <summary>
    /// Brings the bar's record of one live tab group up to date, creating it on
    /// first sight. Groups sit at the top level: they are reopened as a set,
    /// which nesting one inside a folder would obscure.
    /// </summary>
    /// <remarks>
    /// Matched on the session's own group id rather than on the name, so
    /// renaming a group updates its row instead of leaving a stale one beside a
    /// new one. The id lives only as long as the session, which is why a row
    /// that survives to the next run is matched on nothing and simply stays as
    /// the record it already is.
    /// </remarks>
    public SavedTabGroupId MirrorGroup(
        ShellViewModel shell,
        string sessionGroupId,
        SavedTabGroupId savedGroupId,
        string name,
        GroupColor color,
        IReadOnlyList<BookmarkPageCandidate> pages,
        bool replaceWithEmpty = false,
        bool canAdoptIdentifiedExactMatch = false)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(pages);

        var links = GroupFoldersFor(shell);
        links.TryGetValue(sessionGroupId, out var linkedFolder);
        var claimedFolderIds = _groupFolders.Values
            .SelectMany(windowLinks => windowLinks.Values)
            .Select(folder => folder.Id)
            .ToHashSet();
        var result = SavedGroupBookmarkService.Mirror(
            _tree,
            linkedFolder,
            claimedFolderIds,
            savedGroupId,
            name,
            color,
            pages,
            replaceWithEmpty,
            canAdoptIdentifiedExactMatch);

        links[sessionGroupId] = result.Folder;

        if (result.Changed)
        {
            CommitStructuralChange();
        }

        return result.SavedGroupId;
    }
    /// <summary>
    /// Upgrades a legacy saved-group row to a durable identity before it is
    /// reopened. The assigned identity is persisted immediately.
    /// </summary>
    public SavedTabGroupId EnsureSavedGroupId(BookmarkFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (folder.SavedGroupId is { } existing)
        {
            return existing;
        }

        var created = SavedTabGroupId.New();
        folder.MarkAsGroup(folder.GroupColor ?? GroupColor.Slate, created);
        CommitStructuralChange();
        return created;
    }

    /// <summary>
    /// Points a live group at the row it was reopened from, so it updates that
    /// row instead of adding a second one beside it.
    /// </summary>
    /// <remarks>
    /// A row that outlived its session has no live group behind it any more, and
    /// reopening it makes one with a brand new id. Without this the new id would
    /// be unrecognised and mirror itself into a fresh folder, leaving the bar
    /// with two rows for the same set.
    /// </remarks>
    public void AdoptGroupFolder(
        ShellViewModel shell,
        string sessionGroupId,
        BookmarkFolder folder)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(folder);
        var links = GroupFoldersFor(shell);

        // Drop any earlier id pointing at this same folder, so a group reopened
        // twice in one session does not leave a stale entry behind.
        foreach (var stale in links.Where(entry => ReferenceEquals(entry.Value, folder)).ToList())
        {
            links.Remove(stale.Key);
        }

        links[sessionGroupId] = folder;
    }

    /// <summary>
    /// Gives one window exclusive ownership of a saved group's live occurrence.
    /// A durable group has one working tab set; opening it elsewhere moves that
    /// set instead of creating two stale copies that can overwrite each other.
    /// </summary>
    public void ClaimSavedGroup(ShellViewModel target, BookmarkNodeId rowId)
    {
        ArgumentNullException.ThrowIfNull(target);

        foreach (var shell in _shells.Where(shell =>
                     !ReferenceEquals(shell, target) &&
                     shell.IsPrivateWindow == target.IsPrivateWindow).ToList())
        {
            var links = GroupFoldersFor(shell);
            var sessionIds = links
                .Where(entry => entry.Value.Id == rowId)
                .Select(entry => entry.Key)
                .ToList();

            if (sessionIds.Count == 0)
            {
                continue;
            }

            shell.CloseLiveGroupFor(rowId, preserveSavedGroup: true);

            foreach (var sessionId in sessionIds)
            {
                links.Remove(sessionId);
            }
        }
    }

    /// <summary>
    /// Whether this saved group is open in the tab strip of any window, which is
    /// what lets its bar row show that it is already live rather than dormant.
    /// </summary>
    public bool IsLiveAnywhere(BookmarkNodeId rowId) =>
        _shells.Any(shell => shell.HasLiveGroupFor(rowId));

    /// <summary>Redraws the live/dormant state every saved-group row shows.</summary>
    public void RefreshLiveGroupState()
    {
        foreach (var row in GroupItems)
        {
            row.RefreshIsLive();
        }
    }

    /// <summary>The other window currently hosting this saved group, if any.</summary>
    public ShellViewModel? LiveOwnerOf(ShellViewModel requester, BookmarkNodeId rowId)
    {
        ArgumentNullException.ThrowIfNull(requester);
        return _shells.FirstOrDefault(shell =>
            !ReferenceEquals(shell, requester) &&
            shell.IsPrivateWindow == requester.IsPrivateWindow &&
            shell.HasLiveGroupFor(rowId));
    }

    /// <summary>
    /// Removes one group's row, for a group that ceased to exist rather than
    /// being put away.
    /// </summary>
    public void DiscardGroup(
        ShellViewModel shell,
        string sessionGroupId,
        bool removeSavedRowWhenUnlinked = true)
    {
        ArgumentNullException.ThrowIfNull(shell);

        if (!GroupFoldersFor(shell).Remove(sessionGroupId, out var folder))
        {
            return;
        }

        // Private windows may temporarily reopen the same saved group, but they
        // are readers rather than persistence owners. A private occurrence must
        // not keep a deleted normal group alive forever (and the reverse mode is
        // intentionally isolated as well).
        var linkedElsewhere = _groupFolders.Any(entry =>
            entry.Key.IsPrivateWindow == shell.IsPrivateWindow &&
            entry.Value.ContainsValue(folder));

        if (linkedElsewhere || !removeSavedRowWhenUnlinked)
        {
            return;
        }

        RemoveGroupLinks(folder.Id);

        if (_tree.RemoveNode(folder.Id))
        {
            CommitStructuralChange();
        }
    }

    /// <summary>
    /// Deletes a saved group outright: its row and everything in it. The one
    /// action that discards the record, as against closing, which keeps it.
    /// </summary>
    [RelayCommand]
    private void DeleteGroup(BookmarkNodeViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        DeleteSavedGroup(item.Id);
    }

    /// <summary>
    /// Deletes the durable row represented by a live tab group. All windows
    /// linked to that row are closed first, so none can mirror it back later.
    /// </summary>
    public bool DeleteLiveGroup(ShellViewModel shell, string sessionGroupId)
    {
        ArgumentNullException.ThrowIfNull(shell);

        if (!GroupFoldersFor(shell).TryGetValue(sessionGroupId, out var folder))
        {
            return false;
        }

        DeleteSavedGroup(folder.Id);
        return true;
    }

    private void DeleteSavedGroup(BookmarkNodeId rowId)
    {
        // Any live group still pointing at this row is let go of first, so the
        // next mirror does not put the row straight back.
        foreach (var shell in _shells.ToList())
        {
            shell.CloseLiveGroupFor(rowId);
        }

        // Its tabs go too — deleting a group the user is looking at and leaving
        // the tabs behind would be half an action.
        RemoveGroupLinks(rowId);

        if (_tree.RemoveNode(rowId))
        {
            CommitStructuralChange();
        }
    }

    /// <summary>The session group id recorded by a row, when one is live.</summary>
    public string? LiveGroupIdFor(ShellViewModel shell, BookmarkNodeId rowId)
    {
        ArgumentNullException.ThrowIfNull(shell);
        return GroupFoldersFor(shell).FirstOrDefault(entry => entry.Value.Id == rowId).Key;
    }

    /// <summary>The durable row represented by one live session group.</summary>
    public BookmarkNodeId? SavedGroupRowIdFor(ShellViewModel shell, string sessionGroupId)
    {
        ArgumentNullException.ThrowIfNull(shell);
        return GroupFoldersFor(shell).TryGetValue(sessionGroupId, out var folder)
            ? folder.Id
            : null;
    }

    /// <summary>The bar row recording each live group, by the session's group id.</summary>
    private readonly Dictionary<ShellViewModel, Dictionary<string, BookmarkFolder>> _groupFolders = [];

    private Dictionary<string, BookmarkFolder> GroupFoldersFor(ShellViewModel shell)
    {
        if (!_groupFolders.TryGetValue(shell, out var links))
        {
            links = [];
            _groupFolders[shell] = links;
        }

        return links;
    }

    private void RemoveGroupLinks(BookmarkNodeId folderId)
    {
        foreach (var links in _groupFolders.Values)
        {
            foreach (var stale in links.Where(entry => entry.Value.Id == folderId).ToList())
            {
                links.Remove(stale.Key);
            }
        }
    }

    public void Rename(BookmarkNodeId id, string name)
    {
        if (!_tree.RenameNode(id, name))
        {
            return;
        }

        if (_tree.FindFolder(id) is BookmarkFolder { IsSavedGroup: true } folder)
        {
            foreach (var shell in _shells.ToList())
            {
                shell.RenameLiveGroupFor(id, folder.Name);
            }
        }

        CommitStructuralChange();
    }

    /// <summary>Applies one saved group's colour to every open occurrence.</summary>
    public void RecolorSavedGroup(BookmarkNodeId id, GroupColor color)
    {
        if (_tree.FindFolder(id) is not BookmarkFolder { IsSavedGroup: true } folder)
        {
            return;
        }

        folder.MarkAsGroup(color, folder.SavedGroupId);

        foreach (var shell in _shells.ToList())
        {
            shell.RecolorLiveGroupFor(id, color);
        }

        CommitStructuralChange();
    }

    public void Remove(BookmarkNodeId id)
    {
        if (_tree.RemoveNode(id))
        {
            CommitStructuralChange();
        }
    }

    /// <summary>Where a drag ends up. Refused moves leave the tree untouched.</summary>
    public void Move(BookmarkNodeId id, BookmarkFolder newParent, BookmarkNodeId? before = null)
    {
        var moving = _tree.FindNode(id);

        // Saved groups are top-level browser objects, not nestable bookmark
        // folders. Ordinary folders likewise cannot be placed inside a group:
        // a saved group has one flat, deterministic list of pages to reopen.
        if ((moving is BookmarkFolder { IsSavedGroup: true } && !ReferenceEquals(newParent, _tree.Root)) ||
            newParent.IsSavedGroup)
        {
            return;
        }

        if (ReferenceEquals(newParent, _tree.Root))
        {
            var beforeNode = before is { } beforeId ? _tree.FindNode(beforeId) : null;
            var firstOrdinary = _tree.Root.Children.FirstOrDefault(node =>
                node.Id != id && node is not BookmarkFolder { IsSavedGroup: true });

            if (moving is BookmarkFolder { IsSavedGroup: true })
            {
                // The end of the group section is immediately before the first
                // ordinary bookmark, never the physical end of the root list.
                if (beforeNode is not BookmarkFolder { IsSavedGroup: true })
                {
                    before = firstOrdinary?.Id;
                }
            }
            else if (beforeNode is BookmarkFolder { IsSavedGroup: true })
            {
                // A bookmark aimed before a group becomes the first ordinary
                // bookmark instead; saved groups always remain the leading run.
                before = firstOrdinary?.Id;
            }
        }

        if (_tree.MoveNode(id, newParent, before))
        {
            CommitStructuralChange();
        }
    }

    /// <summary>
    /// The folders <paramref name="node"/> could be moved into: every folder in
    /// the tree except the one it is already in, and — for a folder — itself and
    /// its own descendants, which the tree would refuse anyway.
    /// </summary>
    public IReadOnlyList<BookmarkMoveTargetViewModel> BuildMoveTargets(BookmarkNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var targets = new List<BookmarkMoveTargetViewModel>();
        var current = _tree.ParentOf(node.Id);
        var moving = _tree.FindNode(node.Id) as BookmarkFolder;

        Add(_tree.Root, 0);
        return targets;

        void Add(BookmarkFolder folder, int depth)
        {
            var excluded = folder.IsSavedGroup ||
                ReferenceEquals(folder, current) ||
                (moving is not null && (ReferenceEquals(folder, moving) || IsBeneath(folder, moving)));

            if (!excluded)
            {
                targets.Add(new BookmarkMoveTargetViewModel(this, node, folder, depth));
            }

            foreach (var child in folder.Children.OfType<BookmarkFolder>())
            {
                Add(child, depth + 1);
            }
        }

        bool IsBeneath(BookmarkFolder candidate, BookmarkFolder ancestor)
        {
            for (var walk = _tree.ParentOf(candidate.Id); walk is not null; walk = _tree.ParentOf(walk.Id))
            {
                if (ReferenceEquals(walk, ancestor))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public Bookmark? FindByAddress(PageAddress? address) =>
        address is null ? null : _tree.FindByAddress(address);

    public bool IsBookmarked(PageAddress? address) => FindByAddress(address) is not null;

    /// <summary>Shuts every open folder dropdown, at any depth.</summary>
    public void CloseAllFolders()
    {
        foreach (var row in TopLevelRows)
        {
            CloseFolders(row);
        }
    }

    private static void CloseFolders(BookmarkNodeViewModel row)
    {
        row.IsOpen = false;

        if (row.Children is null)
        {
            return;
        }

        foreach (var child in row.Children)
        {
            CloseFolders(child);
        }
    }

    /// <summary>
    /// Brings the rows back in line with the tree and writes it out. Saving on
    /// every change is affordable here in a way it would not be for tab layout:
    /// bookmarks change only when a user deliberately edits them.
    /// </summary>
    private void CommitStructuralChange()
    {
        SyncBarItems();
        _store.Save(Capture());
        BookmarksChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Reconciles the bar's rows against the root's children in place, so a row
    /// that survives keeps its identity, its loaded icon and its open state —
    /// the same approach <see cref="ShellViewModel"/> takes for tabs.
    /// </summary>
    private void SyncBarItems()
    {
        // The root's children are split by kind for display: saved groups to the
        // left of the divider, everything else to the right.
        var groups = _tree.Root.Children.Where(child => child is BookmarkFolder { IsSavedGroup: true }).ToList();
        var rest = _tree.Root.Children.Where(child => child is not BookmarkFolder { IsSavedGroup: true }).ToList();

        Reconcile(GroupItems, groups);
        Reconcile(BarItems, rest);
        OnPropertyChanged(nameof(HasBarItems));
        OnPropertyChanged(nameof(HasGroupItems));
        OnPropertyChanged(nameof(ShowsDivider));
        OnPropertyChanged(nameof(HasAnyItems));

        void Reconcile(ObservableCollection<BookmarkNodeViewModel> rows, List<BookmarkNode> nodes)
        {
            for (var index = 0; index < nodes.Count; index++)
            {
                var child = nodes[index];
                var existing = rows.FirstOrDefault(row => row.Id == child.Id);

                if (existing is null)
                {
                    rows.Insert(index, new BookmarkNodeViewModel(child, this, _favicons));
                    continue;
                }

                if (rows.IndexOf(existing) != index)
                {
                    rows.Move(rows.IndexOf(existing), index);
                }

                existing.Refresh();
            }

            while (rows.Count > nodes.Count)
            {
                rows.RemoveAt(rows.Count - 1);
            }
        }
    }

    private BookmarkSnapshot Capture() => BookmarkSnapshotMapper.Capture(_tree);

    /// <summary>
    /// Rebuilds a tree from a saved snapshot, or returns an empty one when there
    /// is nothing saved. Entries whose address no longer parses are dropped
    /// rather than failing the load — the rest of the bar is still worth having.
    /// </summary>
    public static BookmarkTree Restore(IBookmarkStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var restored = BookmarkSnapshotMapper.Restore(store.Load());

        if (restored.WasRepaired)
        {
            store.Save(BookmarkSnapshotMapper.Capture(restored.Tree));
        }

        return restored.Tree;
    }
}
