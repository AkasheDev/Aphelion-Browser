using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// A paged list of tabs, used both by the overflow panel and the split picker.
/// </summary>
/// <remarks>
/// Twenty rows per page: enough that most windows need only one, few enough that
/// the panel never grows taller than the screen.
/// </remarks>
public sealed partial class TabListViewModel : ViewModelBase
{
    public const int PageSize = 20;

    private readonly List<TabItemViewModel> _all = [];
    private readonly Action<TabItemViewModel> _choose;

    public TabListViewModel(
        string title,
        Action<TabItemViewModel> choose,
        Action? createNew = null,
        Action<TabItemViewModel>? close = null,
        ShellViewModel? owner = null)
    {
        Title = title;
        _choose = choose ?? throw new ArgumentNullException(nameof(choose));
        _createNew = createNew;
        _close = close;
        Owner = owner;
    }

    private readonly Action? _createNew;
    private readonly Action<TabItemViewModel>? _close;

    public string Title { get; }

    /// <summary>
    /// The shell the rows belong to, so a row can offer everything a tab in the
    /// strip offers — grouping, splitting, closing, dragging out. Null for a list
    /// that only picks, such as the split picker.
    /// </summary>
    public ShellViewModel? Owner { get; }

    /// <summary>Whether rows act as tabs rather than only as choices.</summary>
    public bool CanManage => Owner is not null;

    /// <summary>Whether this list fills the persistent Other Tabs drawer.</summary>
    public bool IsDrawer => Owner is not null;

    /// <summary>Whether the list offers a "new tab" row alongside the existing tabs.</summary>
    public bool CanCreateNew => _createNew is not null;

    /// <summary>
    /// Whether rows can be closed from here. The overflow panel allows it; the
    /// split picker does not, since it is asking which tab to pair with.
    /// </summary>
    public bool CanClose => _close is not null;

    /// <summary>The rows on the current page.</summary>
    public ObservableCollection<TabItemViewModel> Page { get; } = [];

    [ObservableProperty]
    private int _pageIndex;

    public int PageCount => Math.Max(1, (int)Math.Ceiling(_all.Count / (double)PageSize));

    public bool HasPages => PageCount > 1;

    public string PageLabel => $"{PageIndex + 1} / {PageCount}";

    /// <summary>
    /// True when there is nothing at all to offer. A list that can create a new
    /// tab is never empty in this sense — that row is always something to pick.
    /// </summary>
    public bool IsEmpty => _all.Count == 0 && !CanCreateNew;

    public bool CanGoPrevious => PageIndex > 0;

    public bool CanGoNext => PageIndex < PageCount - 1;

    /// <summary>
    /// The first item on the following page, used as the identity boundary when a
    /// row is dropped below the current page.
    /// </summary>
    public TabItemViewModel? ItemAfterCurrentPage
    {
        get
        {
            var index = (PageIndex + 1) * PageSize;
            return index < _all.Count ? _all[index] : null;
        }
    }

    /// <summary>Replaces the contents, keeping the current page where possible.</summary>
    public void SetItems(IEnumerable<TabItemViewModel> items)
    {
        _all.Clear();
        _all.AddRange(items.DistinctBy(item => item.Id));

        PageIndex = Math.Clamp(PageIndex, 0, PageCount - 1);
        RefreshPage();
    }

    [RelayCommand]
    private void Choose(TabItemViewModel? item)
    {
        if (item is not null)
        {
            _choose(item);
        }
    }

    [RelayCommand]
    private void CreateNew() => _createNew?.Invoke();

    [RelayCommand]
    private void Close(TabItemViewModel? item)
    {
        if (item is not null)
        {
            _close?.Invoke(item);
        }
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CanGoPrevious)
        {
            PageIndex--;
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CanGoNext)
        {
            PageIndex++;
        }
    }

    partial void OnPageIndexChanged(int value) => RefreshPage();

    private void RefreshPage()
    {
        var desired = _all
            .Skip(PageIndex * PageSize)
            .Take(PageSize)
            .ToList();

        // Self-heal any stale duplicate occurrence left by an interrupted routed
        // gesture or an older build. A tab identity may appear at most once on a
        // page even if a caller accidentally supplied the same row twice.
        var seen = new HashSet<Aphelion.Desktop.Domain.ValueObjects.TabId>();

        for (var i = 0; i < Page.Count;)
        {
            if (seen.Add(Page[i].Id))
            {
                i++;
            }
            else
            {
                Page.RemoveAt(i);
            }
        }

        // Preserve row instances and their routed gesture while the shell syncs.
        // Clearing and rebuilding during a pointer release leaves stale visuals
        // on the active event route and is perceived as duplicated rows.
        for (var i = Page.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(Page[i]))
            {
                Page.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var current = Page.IndexOf(desired[i]);

            if (current < 0)
            {
                Page.Insert(i, desired[i]);
            }
            else if (current != i)
            {
                Page.Move(current, i);
            }
        }

        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(ItemAfterCurrentPage));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }
}
