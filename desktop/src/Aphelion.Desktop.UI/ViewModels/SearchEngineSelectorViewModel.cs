using System.Collections.ObjectModel;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>Presentation state for the persistent New Tab search-engine picker.</summary>
public sealed partial class SearchEngineSelectorViewModel : ViewModelBase
{
    private readonly ISearchEnginePreference _preference;

    public SearchEngineSelectorViewModel(
        ISearchEnginePreference preference,
        IFaviconLoader favicons)
    {
        _preference = preference ?? throw new ArgumentNullException(nameof(preference));
        Options =
        [
            new(SearchEngineKind.Google, "Google", "https://www.google.com/", favicons),
            new(SearchEngineKind.Yandex, "Yandex", "https://yandex.com/", favicons),
            new(SearchEngineKind.DuckDuckGo, "DuckDuckGo", "https://duckduckgo.com/", favicons),
            new(SearchEngineKind.Bing, "Bing", "https://www.bing.com/", favicons),
            new(SearchEngineKind.Brave, "Brave Search", "https://search.brave.com/", favicons),
            new(SearchEngineKind.Yahoo, "Yahoo", "https://search.yahoo.com/", favicons),
        ];
        Selected = FindSelected();
        _preference.Changed += (_, _) => Selected = FindSelected();
    }

    public ObservableCollection<SearchEngineOptionViewModel> Options { get; }

    [ObservableProperty]
    private SearchEngineOptionViewModel _selected;

    [ObservableProperty]
    private bool _isOpen;

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void Select(SearchEngineOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        _preference.ChangeTo(option.Kind);
        Selected = FindSelected();
        IsOpen = false;
    }

    private SearchEngineOptionViewModel FindSelected() =>
        Options.First(option => option.Kind == _preference.Selected);
}
