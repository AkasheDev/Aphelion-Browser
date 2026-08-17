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
    }

    public IReadOnlyList<ThemePreference> Themes { get; } =
        [ThemePreference.System, ThemePreference.Light, ThemePreference.Dark];

    public IReadOnlyList<StartupMode> StartupModes { get; } =
        [StartupMode.RestoreSession, StartupMode.NewTab, StartupMode.CustomPages];

    public IReadOnlyList<UpdateChannel> UpdateChannels { get; } =
        [UpdateChannel.Stable, UpdateChannel.Beta];

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
        Persist();
        ThemeController.Apply(value);
    }

    partial void OnStartupChanged(StartupMode value)
    {
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

    partial void OnUpdateChannelChanged(UpdateChannel value) => Persist();

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
