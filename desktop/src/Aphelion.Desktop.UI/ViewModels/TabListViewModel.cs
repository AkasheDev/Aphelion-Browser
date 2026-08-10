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

    /// <summary>Replaces the contents, keeping the current page where possible.</summary>
    public void SetItems(IEnumerable<TabItemViewModel> items)
    {
        _all.Clear();
        _all.AddRange(items);

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
        Page.Clear();

        foreach (var item in _all.Skip(PageIndex * PageSize).Take(PageSize))
        {
            Page.Add(item);
        }

        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }
}
