using System.Collections.ObjectModel;
using System.Diagnostics;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Enums;
using Aphelion.Desktop.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>The <c>aphelion://settings</c> page.</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsStore _settings;
    private readonly IPrivacyPreferenceStore _privacy;
    private readonly ISearchEnginePreference _search;
    private readonly IPasswordStore _passwords;
    private readonly IFileExplorer _files;

    public SettingsViewModel(
        IAppSettingsStore settings,
        IPrivacyPreferenceStore privacy,
        ISearchEnginePreference search,
        IPasswordStore passwords,
        IFileExplorer files)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _privacy = privacy ?? throw new ArgumentNullException(nameof(privacy));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _passwords = passwords ?? throw new ArgumentNullException(nameof(passwords));
        _files = files ?? throw new ArgumentNullException(nameof(files));

        var current = _settings.Load();
        _theme = current.Theme;
        _startup = current.Startup;
        _startupPagesText = string.Join(Environment.NewLine, current.StartupPages);
        _downloadFolder = current.DownloadFolder ?? string.Empty;
        _updateChannel = current.UpdateChannel;
        var privacyPrefs = _privacy.Load();
        _weatherEnabled = privacyPrefs.WeatherEnabled;
        _suggestionsEnabled = privacyPrefs.SearchSuggestionsEnabled;
        _selectedEngine = _search.Selected;
        ReloadPasswords();

        SelectedSection = Sections[0];
        Passwords.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPasswords));
            OnPropertyChanged(nameof(PasswordCountLabel));
        };
    }

    /// <summary>
    /// The left rail, as Chrome has it: one category at a time rather than a
    /// single column the user has to scroll past everything to reach.
    /// </summary>
    public IReadOnlyList<SettingsSectionViewModel> Sections { get; } =
    [
        new(SettingsSection.Appearance, "Appearance", "Colour, theme and how the browser looks",
            "M12,3 A9,9 0 1 0 12,21 C13.1,21 14,20.1 14,19 C14,18.5 13.8,18 13.5,17.6 C13.2,17.3 13,16.8 13,16.3 C13,15.2 13.9,14.3 15,14.3 H17 C19.2,14.3 21,12.5 21,10.3 C21,6.3 17,3 12,3 M7.5,11 A1,1 0 1 0 7.5,11.01 M11,7.5 A1,1 0 1 0 11,7.51 M16,8.5 A1,1 0 1 0 16,8.51"),
        new(SettingsSection.Startup, "On startup", "What opens when Aphelion launches",
            "M12,3 V12 M7.5,5.5 A8,8 0 1 0 16.5,5.5"),
        new(SettingsSection.Search, "Search", "The engine behind the address bar",
            "M11,4 A7,7 0 1 0 11,18 A7,7 0 1 0 11,4 M16,16 L21,21"),
        new(SettingsSection.NewTab, "New Tab", "The page every new tab opens on",
            "M4,5 H20 V19 H4 Z M4,9 H20 M12,12 V16 M10,14 H14"),
        new(SettingsSection.Downloads, "Downloads", "Where saved files go",
            "M12,4 V15 M8,11 L12,15 L16,11 M5,19 H19"),
        new(SettingsSection.Passwords, "Passwords", "Credentials kept for you",
            "M7,10 V7.5 A5,5 0 0 1 17,7.5 V10 M5.5,10 H18.5 V20 H5.5 Z M12,13.5 V16.5"),
        new(SettingsSection.System, "System", "Default browser and updates",
            "M4,6 H20 V16 H4 Z M9,20 H15 M12,16 V20"),
        new(SettingsSection.About, "About Aphelion", "Version and release channel",
            "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,11 V16.5 M12,7.6 V7.8"),
    ];

    [ObservableProperty]
    private SettingsSectionViewModel? _selectedSection;

    partial void OnSelectedSectionChanged(SettingsSectionViewModel? value)
    {
        OnPropertyChanged(nameof(ShowsAppearance));
        OnPropertyChanged(nameof(ShowsStartup));
        OnPropertyChanged(nameof(ShowsSearch));
        OnPropertyChanged(nameof(ShowsNewTab));
        OnPropertyChanged(nameof(ShowsDownloads));
        OnPropertyChanged(nameof(ShowsPasswords));
        OnPropertyChanged(nameof(ShowsSystem));
        OnPropertyChanged(nameof(ShowsAbout));
    }

    private bool Showing(SettingsSection section) => SelectedSection?.Key == section;

    public bool ShowsAppearance => Showing(SettingsSection.Appearance);

    public bool ShowsStartup => Showing(SettingsSection.Startup);

    public bool ShowsSearch => Showing(SettingsSection.Search);

    public bool ShowsNewTab => Showing(SettingsSection.NewTab);

    public bool ShowsDownloads => Showing(SettingsSection.Downloads);

    public bool ShowsPasswords => Showing(SettingsSection.Passwords);

    public bool ShowsSystem => Showing(SettingsSection.System);

    public bool ShowsAbout => Showing(SettingsSection.About);

    /// <summary>Shown on the About card, read from the assembly rather than typed twice.</summary>
    public string AppVersion { get; } =
        typeof(SettingsViewModel).Assembly.GetName().Version is { } version
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "0.1.0";

    public bool HasPasswords => Passwords.Count > 0;

    public string PasswordCountLabel => Passwords.Count switch
    {
        0 => "Nothing saved yet",
        1 => "1 saved credential",
        var count => $"{count} saved credentials",
    };

    public IReadOnlyList<Choice<ThemePreference>> ThemeChoices { get; } =
    [
        new(ThemePreference.System, "Match system"),
        new(ThemePreference.Light, "Light"),
        new(ThemePreference.Dark, "Dark"),
    ];

    public IReadOnlyList<Choice<StartupMode>> StartupChoices { get; } =
    [
        new(StartupMode.RestoreSession, "Restore previous session"),
        new(StartupMode.NewTab, "Open the New Tab page"),
        new(StartupMode.CustomPages, "Open specific pages"),
    ];

    public IReadOnlyList<Choice<UpdateChannel>> UpdateChannelChoices { get; } =
    [
        new(UpdateChannel.Stable, "Stable"),
        new(UpdateChannel.Beta, "Beta"),
    ];

    public Choice<ThemePreference>? SelectedTheme
    {
        get => ThemeChoices.First(choice => choice.Value == Theme);
        set
        {
            if (value is not null)
            {
                Theme = value.Value;
            }
        }
    }

    public Choice<StartupMode>? SelectedStartup
    {
        get => StartupChoices.First(choice => choice.Value == Startup);
        set
        {
            if (value is not null)
            {
                Startup = value.Value;
            }
        }
    }

    public Choice<UpdateChannel>? SelectedUpdateChannel
    {
        get => UpdateChannelChoices.First(choice => choice.Value == UpdateChannel);
        set
        {
            if (value is not null)
            {
                UpdateChannel = value.Value;
            }
        }
    }

    public IReadOnlyList<SearchEngineKind> SearchEngines { get; } =
        Enum.GetValues<SearchEngineKind>();

    [ObservableProperty]
    private ThemePreference _theme;

    [ObservableProperty]
    private StartupMode _startup;

    [ObservableProperty]
    private string _startupPagesText = string.Empty;

    [ObservableProperty]
    private SearchEngineKind _selectedEngine;

    [ObservableProperty]
    private bool _weatherEnabled;

    [ObservableProperty]
    private bool _suggestionsEnabled;

    [ObservableProperty]
    private string _downloadFolder = string.Empty;

    [ObservableProperty]
    private UpdateChannel _updateChannel;

    [ObservableProperty]
    private string _newPasswordHost = string.Empty;

    [ObservableProperty]
    private string _newPasswordUsername = string.Empty;

    [ObservableProperty]
    private string _newPasswordSecret = string.Empty;

    public ObservableCollection<SavedCredential> Passwords { get; } = [];

    public bool ShowsCustomPages => Startup == StartupMode.CustomPages;

    partial void OnThemeChanged(ThemePreference value)
    {
        OnPropertyChanged(nameof(SelectedTheme));
        Persist();
        ThemeController.Apply(value);
    }

    partial void OnStartupChanged(StartupMode value)
    {
        OnPropertyChanged(nameof(SelectedStartup));
        OnPropertyChanged(nameof(ShowsCustomPages));
        Persist();
    }

    partial void OnStartupPagesTextChanged(string value) => Persist();

    partial void OnSelectedEngineChanged(SearchEngineKind value) => _search.ChangeTo(value);

    partial void OnWeatherEnabledChanged(bool value) =>
        _privacy.Save(_privacy.Load() with { WeatherEnabled = value });

    partial void OnSuggestionsEnabledChanged(bool value) =>
        _privacy.Save(_privacy.Load() with { SearchSuggestionsEnabled = value });

    partial void OnDownloadFolderChanged(string value) => Persist();

    partial void OnUpdateChannelChanged(UpdateChannel value)
    {
        OnPropertyChanged(nameof(SelectedUpdateChannel));
        Persist();
    }

    [RelayCommand]
    private void UseDefaultDownloadFolder() => DownloadFolder = string.Empty;

    [RelayCommand]
    private static void OpenDefaultApps()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open", "-b com.apple.systempreferences")
                {
                    UseShellExecute = true,
                });
                return;
            }

            Process.Start(new ProcessStartInfo("xdg-settings", "set default-web-browser aphelion.desktop")
            {
                UseShellExecute = false,
            });
        }
        catch (Exception)
        {
            // Opening the OS settings panel is best effort.
        }
    }

    [RelayCommand]
    private void AddPassword()
    {
        if (string.IsNullOrWhiteSpace(NewPasswordHost) ||
            string.IsNullOrWhiteSpace(NewPasswordUsername) ||
            string.IsNullOrWhiteSpace(NewPasswordSecret))
        {
            return;
        }

        Passwords.Insert(
            0,
            new SavedCredential(NewPasswordHost.Trim(), NewPasswordUsername.Trim(), NewPasswordSecret));
        NewPasswordHost = string.Empty;
        NewPasswordUsername = string.Empty;
        NewPasswordSecret = string.Empty;
        PersistPasswords();
    }

    [RelayCommand]
    private void RemovePassword(SavedCredential? credential)
    {
        if (credential is null)
        {
            return;
        }

        Passwords.Remove(credential);
        PersistPasswords();
    }

    public string PreferredDownloadDirectory =>
        string.IsNullOrWhiteSpace(DownloadFolder) ? _files.DownloadsDirectory : DownloadFolder;

    private void ReloadPasswords()
    {
        Passwords.Clear();
        foreach (var row in _passwords.Load())
        {
            Passwords.Add(row);
        }
    }

    private void PersistPasswords() => _passwords.SaveAll(Passwords.ToList());

    private void Persist()
    {
        var pages = StartupPagesText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        _settings.Save(new AppSettings(
            Theme,
            Startup,
            pages,
            string.IsNullOrWhiteSpace(DownloadFolder) ? null : DownloadFolder.Trim(),
            UpdateChannel,
            _settings.Load().HasCompletedOnboarding));
    }
}

/// <summary>A settings combo item that shows a phrase instead of the enum name.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>The categories in the settings rail.</summary>
public enum SettingsSection
{
    Appearance,
    Startup,
    Search,
    NewTab,
    Downloads,
    Passwords,
    System,
    About,
}

/// <summary>
/// One row of the settings rail. The glyph travels with the category rather
/// than being spelled out beside every nav item in the view.
/// </summary>
public sealed record SettingsSectionViewModel(
    SettingsSection Key,
    string Label,
    string Summary,
    string Glyph);
