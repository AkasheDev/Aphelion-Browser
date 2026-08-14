using System.Collections.ObjectModel;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// One row in the bookmark bar or in an open folder. A view over a
/// <see cref="BookmarkNode"/>; the node itself remains the source of truth.
/// </summary>
/// <remarks>
/// One class covers bookmarks and folders alike. The row template switches on
/// <see cref="IsFolder"/> for its icon and its click behaviour, which is less
/// machinery than two view-model types and a template selector for what is one
/// row with one conditional part.
/// </remarks>
public sealed partial class BookmarkNodeViewModel : ViewModelBase
{
    private readonly IFaviconLoader? _favicons;

    /// <summary>The icon address already loaded, so it is fetched only once.</summary>
    private string? _loadedIconKey;

    public BookmarkNodeViewModel(
        BookmarkNode node,
        BookmarksViewModel owner,
        IFaviconLoader? favicons = null,
        bool isInFolder = false)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _favicons = favicons;
        _name = node.Name;
        IsInFolder = isInFolder;

        if (node is BookmarkFolder { IsSavedGroup: false } folder)
        {
            Children = [];
            SyncChildren(folder);
        }

        LoadFaviconIfChanged();
    }

    /// <summary>
    /// Whether this row is inside a folder's dropdown rather than on the bar.
    /// Decides which way its submenu opens and which way a drop marker lies.
    /// </summary>
    public bool IsInFolder { get; }

    /// <summary>
    /// Where this folder's dropdown appears: below a row on the bar, and beside
    /// one inside a dropdown, as nested menus do everywhere.
    /// </summary>
    public PlacementMode FolderPlacement =>
        IsInFolder ? PlacementMode.RightEdgeAlignedTop : PlacementMode.BottomEdgeAlignedLeft;

    /// <summary>
    /// Whether to draw the chevron pointing at the submenu. Only inside a
    /// dropdown: on the bar the folder glyph already identifies the row. A saved
    /// group never shows one, since clicking it reopens its pages rather than
    /// expanding into a menu.
    /// </summary>
    public bool ShowsSideChevron => CanExpandAsFolder && IsInFolder;

    /// <summary>
    /// Leaves room for the drop marker that sits in front of the row, on
    /// whichever side the rows are stacked from.
    /// </summary>
    public Avalonia.Thickness RowInset => IsInFolder ? new(0, 3, 0, 0) : new(3, 0, 0, 0);

    public BookmarkNode Node { get; }

    /// <summary>
    /// Commands shared by the bar and the folder popups. A popup lives in its own
    /// visual tree, where ancestor bindings cannot reach the bar's view model, so
    /// each row carries its command owner explicitly — the same reason
    /// <see cref="TabItemViewModel.Owner"/> exists.
    /// </summary>
    public BookmarksViewModel Owner { get; }

    public BookmarkNodeId Id => Node.Id;

    public bool IsFolder => Node is BookmarkFolder;

    /// <summary>Whether this row is a saved tab group rather than a plain folder.</summary>
    public bool IsSavedGroup => Node is BookmarkFolder { IsSavedGroup: true };

    /// <summary>Only ordinary folders own a dropdown; saved groups reopen tabs.</summary>
    public bool CanExpandAsFolder => IsFolder && !IsSavedGroup;

    /// <summary>The colour a saved group is drawn in, or null for anything else.</summary>
    public IBrush? GroupBrush => Node is BookmarkFolder { GroupColor: { } color }
        ? GroupBrushes.For(color)
        : null;

    /// <summary>
    /// Whether this saved group is currently open in some window's tab strip.
    /// Its row is outlined in the group's colour while it is, so the bar shows
    /// which groups are already live and which are only stored.
    /// </summary>
    public bool IsLive => IsSavedGroup && Owner.IsLiveAnywhere(Node.Id);

    /// <summary>
    /// The outline a live saved group wears, and a transparent brush otherwise.
    /// The ring is drawn at a constant thickness and only changes colour, so a
    /// group going live never reflows the row beside it.
    /// </summary>
    public IBrush LiveRingBrush => IsLive && GroupBrush is { } brush
        ? brush
        : Brushes.Transparent;

    /// <summary>
    /// The context menu of a saved group: its actions, then the pages it would
    /// reopen, as Chrome lists them.
    /// </summary>
    /// <remarks>
    /// Built on demand rather than kept, so the list is whatever the group holds
    /// at the moment it is opened. The page rows are real row view models, which
    /// is what lets each one carry its own favicon as it loads.
    /// </remarks>
    public IReadOnlyList<GroupMenuEntryViewModel> GroupMenu
    {
        get
        {
            if (Node is not BookmarkFolder { IsSavedGroup: true } group)
            {
                return [];
            }

            var entries = new List<GroupMenuEntryViewModel>
            {
                GroupMenuEntryViewModel.Action("Open group", Owner.OpenInCommand, OpenHere),
                GroupMenuEntryViewModel.Action("Open group in new window", Owner.OpenInCommand, OpenNewWindow),
                GroupMenuEntryViewModel.Separator(),
                GroupMenuEntryViewModel.Action("Rename…", Owner.BeginRenameCommand, this),
                GroupMenuEntryViewModel.Action("Delete group", Owner.DeleteGroupCommand, this),
            };

            var pages = BookmarkTree.PagesOf(group);

            if (pages.Count == 0)
            {
                return entries;
            }

            entries.Add(GroupMenuEntryViewModel.Separator());
            entries.Add(GroupMenuEntryViewModel.Heading("Tabs in group"));

            foreach (var page in pages)
            {
                var row = new BookmarkNodeViewModel(page, Owner, _favicons, isInFolder: true);

                // A page opens on its own; a New Tab slot has no address of its
                // own, so it reopens the group it belongs to rather than sitting
                // in the list as a row that does nothing.
                entries.Add(GroupMenuEntryViewModel.ForPage(
                    row,
                    Owner.OpenInCommand,
                    page is Bookmark ? row.OpenNewTab : OpenHere));
            }

            return entries;
        }
    }

    /// <summary>Re-reads <see cref="IsLive"/> after tabs or groups change.</summary>
    public void RefreshIsLive()
    {
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(LiveRingBrush));
    }

    /// <summary>How many pages a saved group would reopen, for its menu.</summary>
    public int PageCount => Node switch
    {
        // New Tab slots are tabs the group will reopen, so they count toward what
        // the menu promises.
        BookmarkFolder { IsSavedGroup: true } savedGroup =>
            BookmarkTree.PagesOf(savedGroup).Count,
        BookmarkFolder folder => BookmarkTree.Descendants(folder).OfType<Bookmark>().Count(),
        _ => 0,
    };

    public string GroupToolTip => $"{Name} · {PageCount} {(PageCount == 1 ? "tab" : "tabs")}";

    /// <summary>Ordinary folders show the folder glyph; saved groups show a dot.</summary>
    public bool ShowsFolderGlyph => IsFolder && !IsSavedGroup;

    /// <summary>The folder's contents, or null for a bookmark.</summary>
    public ObservableCollection<BookmarkNodeViewModel>? Children { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFavicon))]
    private Bitmap? _favicon;

    public bool HasFavicon => Favicon is not null;

    /// <summary>Whether this folder's dropdown is showing.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>The address shown as a tooltip, empty for a folder.</summary>
    public string AddressText => Node is Bookmark bookmark ? bookmark.Address.ToString() : string.Empty;

    /// <summary>
    /// The folders this row could be filed into, for its "Move to" menu. Built
    /// on demand so it reflects the tree as it stands when the menu is opened.
    /// </summary>
    public IReadOnlyList<BookmarkMoveTargetViewModel> MoveTargets => Owner.BuildMoveTargets(this);

    // The open menu. A folder opens everything it holds, so its entries say how
    // many pages that is; a single bookmark needs no count.
    private string Suffix => IsFolder ? $" all ({PageCount})" : string.Empty;

    public string OpenHereLabel => IsFolder ? $"Open{Suffix}" : "Open";

    public string OpenNewTabLabel => IsFolder ? $"Open{Suffix} in new tabs" : "Open in new tab";

    public string OpenNewWindowLabel => IsFolder ? $"Open{Suffix} in new window" : "Open in new window";

    public string OpenPrivateLabel => IsFolder ? $"Open{Suffix} in private window" : "Open in private window";

    public BookmarkOpenRequest OpenHere => new(this, BookmarkOpenTarget.CurrentTab);

    public BookmarkOpenRequest OpenNewTab => new(this, BookmarkOpenTarget.NewTab);

    public BookmarkOpenRequest OpenNewWindow => new(this, BookmarkOpenTarget.NewWindow);

    public BookmarkOpenRequest OpenPrivate => new(this, BookmarkOpenTarget.PrivateWindow);

    public BookmarkOpenRequest OpenSplit => new(this, BookmarkOpenTarget.SplitPane);

    /// <summary>Split view takes one page, so only a bookmark offers it.</summary>
    public bool ShowsSplitOption => !IsFolder && !IsSavedGroup;

    /// <summary>Re-reads everything this row draws from the node behind it.</summary>
    public void Refresh()
    {
        Name = Node.Name;
        OnPropertyChanged(nameof(AddressText));
        OnPropertyChanged(nameof(IsFolder));
        OnPropertyChanged(nameof(IsSavedGroup));
        OnPropertyChanged(nameof(CanExpandAsFolder));
        OnPropertyChanged(nameof(GroupBrush));
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(LiveRingBrush));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(GroupToolTip));
        OnPropertyChanged(nameof(ShowsFolderGlyph));
        OnPropertyChanged(nameof(ShowsSideChevron));
        OnPropertyChanged(nameof(OpenHereLabel));
        OnPropertyChanged(nameof(OpenNewTabLabel));
        OnPropertyChanged(nameof(OpenNewWindowLabel));
        OnPropertyChanged(nameof(OpenPrivateLabel));
        OnPropertyChanged(nameof(ShowsSplitOption));

        if (Node is BookmarkFolder { IsSavedGroup: false } folder)
        {
            SyncChildren(folder);
        }

        LoadFaviconIfChanged();
    }

    /// <summary>
    /// Reconciles the child rows against the folder in place, so rows that are
    /// still present keep their identity — and with it their loaded icon and any
    /// open dropdown — instead of being rebuilt on every change.
    /// </summary>
    private void SyncChildren(BookmarkFolder folder)
    {
        if (Children is null)
        {
            return;
        }

        for (var index = 0; index < folder.Children.Count; index++)
        {
            var child = folder.Children[index];
            var existing = Children.FirstOrDefault(row => row.Id == child.Id);

            if (existing is null)
            {
                Children.Insert(index, new BookmarkNodeViewModel(child, Owner, _favicons, isInFolder: true));
                continue;
            }

            if (Children.IndexOf(existing) != index)
            {
                Children.Move(Children.IndexOf(existing), index);
            }

            existing.Refresh();
        }

        while (Children.Count > folder.Children.Count)
        {
            Children.RemoveAt(Children.Count - 1);
        }
    }

    private async void LoadFaviconIfChanged()
    {
        if (Node is not Bookmark bookmark || _favicons is null)
        {
            return;
        }

        var source = bookmark.FaviconSource;
        var key = source.ToString();

        if (key == _loadedIconKey)
        {
            return;
        }

        _loadedIconKey = key;

        var bytes = await _favicons.LoadAsync(source).ConfigureAwait(true);

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
        }
    }
}
