using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Enums;
using Aphelion.Desktop.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>First-run choices that become the profile's settings.</summary>
public sealed partial class OnboardingViewModel : ViewModelBase
{
    private readonly IAppSettingsStore _settings;
    private readonly ISearchEnginePreference _search;
    private readonly Action _finished;

    public OnboardingViewModel(
        IAppSettingsStore settings,
        ISearchEnginePreference search,
        Action finished)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _finished = finished ?? throw new ArgumentNullException(nameof(finished));
        _theme = _settings.Load().Theme;
        _selectedEngine = _search.Selected;
    }

    public IReadOnlyList<ThemePreference> Themes { get; } =
        [ThemePreference.System, ThemePreference.Light, ThemePreference.Dark];

    public IReadOnlyList<SearchEngineKind> SearchEngines { get; } =
        Enum.GetValues<SearchEngineKind>();

    [ObservableProperty]
    private ThemePreference _theme;

    [ObservableProperty]
    private SearchEngineKind _selectedEngine;

    [RelayCommand]
    private void Finish()
    {
        _search.ChangeTo(SelectedEngine);
        var current = _settings.Load();
        _settings.Save(current with
        {
            Theme = Theme,
            HasCompletedOnboarding = true,
        });
        ThemeController.Apply(Theme);
        _finished();
    }
}
