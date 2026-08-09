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

    public TabListViewModel(string title, Action<TabItemViewModel> choose)
    {
        Title = title;
        _choose = choose ?? throw new ArgumentNullException(nameof(choose));
    }

    public string Title { get; }

    /// <summary>The rows on the current page.</summary>
    public ObservableCollection<TabItemViewModel> Page { get; } = [];

    [ObservableProperty]
    private int _pageIndex;

    public int PageCount => Math.Max(1, (int)Math.Ceiling(_all.Count / (double)PageSize));

    public bool HasPages => PageCount > 1;

    public string PageLabel => $"{PageIndex + 1} / {PageCount}";

    public bool IsEmpty => _all.Count == 0;

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
