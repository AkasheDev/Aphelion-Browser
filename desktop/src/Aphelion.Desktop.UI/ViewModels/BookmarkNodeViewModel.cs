using System.Collections.ObjectModel;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using Avalonia.Controls;
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

        if (node is BookmarkFolder folder)
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
    /// dropdown: on the bar the folder glyph already identifies the row.
    /// </summary>
    public bool ShowsSideChevron => IsFolder && IsInFolder;

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

    /// <summary>
    /// Whether a drag would land in front of this row, drawn as a marker to its
    /// left.
    /// </summary>
    [ObservableProperty]
    private bool _isDropBefore;

    /// <summary>
    /// Whether a drag would be filed into this folder, drawn by highlighting the
    /// whole row.
    /// </summary>
    [ObservableProperty]
    private bool _isDropTarget;

    /// <summary>The address shown as a tooltip, empty for a folder.</summary>
    public string AddressText => Node is Bookmark bookmark ? bookmark.Address.ToString() : string.Empty;

    /// <summary>
    /// The folders this row could be filed into, for its "Move to" menu. Built
    /// on demand so it reflects the tree as it stands when the menu is opened.
    /// </summary>
    public IReadOnlyList<BookmarkMoveTargetViewModel> MoveTargets => Owner.BuildMoveTargets(this);

    /// <summary>Re-reads everything this row draws from the node behind it.</summary>
    public void Refresh()
    {
        Name = Node.Name;
        OnPropertyChanged(nameof(AddressText));

        if (Node is BookmarkFolder folder)
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
