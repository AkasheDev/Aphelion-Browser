using System.Collections.ObjectModel;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>Interaction state for the search-and-launcher-only New Tab surface.</summary>
public sealed partial class NewTabPageViewModel : ViewModelBase
{
    private static readonly ObservableCollection<object> EmptyTiles = [];

    private readonly NewTabShortcutHub? _shortcuts;
    private readonly Action<PageAddress> _navigate;

    public NewTabPageViewModel(
        NewTabShortcutHub? shortcuts,
        NewTabAmbientViewModel? ambient,
        SearchEngineSelectorViewModel? searchEngines,
        ISearchSuggestionProvider? suggestions,
        Func<string, bool> search,
        Action<PageAddress> navigate,
        bool isPrivate = false,
        IPrivacyPreferenceStore? privacy = null)
    {
        _shortcuts = shortcuts;
        _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        Ambient = ambient;
        SearchEngines = searchEngines;
        Search = new NewTabSearchBoxViewModel(suggestions, searchEngines, search, privacy);
        IsPrivate = isPrivate;

        if (Ambient is not null)
        {
            Ambient.PropertyChanged += OnAmbientPropertyChanged;
        }

        Search.PropertyChanged += OnSearchPropertyChanged;
    }

    public NewTabAmbientViewModel? Ambient { get; }

    public SearchEngineSelectorViewModel? SearchEngines { get; }

    public NewTabSearchBoxViewModel Search { get; }

    public bool IsPrivate { get; }

    public ObservableCollection<object> ShortcutTiles => IsPrivate ? EmptyTiles : _shortcuts?.Tiles ?? EmptyTiles;

    /// <summary>
    /// Whether the small "Weather off" label offering to turn it back on should
    /// show. Private mode carries neither state of the weather card — see the
    /// remarks in NewTabPage.axaml — so this is false there even though
    /// <c>Ambient.IsWeatherEnabled</c> is also false.
    /// </summary>
    public bool ShowWeatherOffLabel => !IsPrivate && Ambient?.IsWeatherEnabled == false;

    /// <summary>
    /// Whether the shortcut row is showing. It and the suggestion list share the
    /// lower half of the page, so the shortcuts step aside while suggestions are
    /// up — the mark, the greeting and the search box above are untouched and
    /// stay exactly where they are.
    /// </summary>
    public bool ShowShortcutRow => !IsPrivate && !Search.HasSuggestions;

    private void OnAmbientPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NewTabAmbientViewModel.IsWeatherEnabled))
        {
            OnPropertyChanged(nameof(ShowWeatherOffLabel));
        }
    }

    private void OnSearchPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NewTabSearchBoxViewModel.HasSuggestions))
        {
            OnPropertyChanged(nameof(ShowShortcutRow));
        }
    }

    [ObservableProperty]
    private bool _isEditorOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorHeading))]
    private NewTabShortcutViewModel? _editingShortcut;

    [ObservableProperty]
    private string _editorName = string.Empty;

    [ObservableProperty]
    private string _editorAddress = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorError))]
    private string? _editorError;

    public string EditorHeading => EditingShortcut is null ? "Add shortcut" : "Edit shortcut";

    public bool HasEditorError => !string.IsNullOrWhiteSpace(EditorError);

    [RelayCommand]
    private void OpenShortcut(NewTabShortcutViewModel? shortcut)
    {
        if (shortcut is not null)
        {
            _navigate(shortcut.Address);
        }
    }

    [RelayCommand]
    private void BeginAddShortcut()
    {
        EditingShortcut = null;
        EditorName = string.Empty;
        EditorAddress = string.Empty;
        EditorError = null;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void BeginRenameShortcut(NewTabShortcutViewModel? shortcut)
    {
        if (shortcut is null)
        {
            return;
        }

        EditingShortcut = shortcut;
        EditorName = shortcut.Name;
        EditorAddress = shortcut.Address.ToString();
        EditorError = null;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void SaveShortcut()
    {
        if (_shortcuts is null)
        {
            EditorError = "Shortcut management is unavailable.";
            return;
        }

        bool saved;
        string? error;

        if (EditingShortcut is null)
        {
            saved = _shortcuts.TryAdd(EditorName, EditorAddress, out error);
        }
        else
        {
            saved = _shortcuts.TryUpdate(EditingShortcut, EditorName, EditorAddress, out error);
        }

        if (!saved)
        {
            EditorError = error;
            return;
        }

        CloseEditor();
    }

    [RelayCommand]
    private void CancelEditor() => CloseEditor();

    [RelayCommand]
    private void RemoveShortcut(NewTabShortcutViewModel? shortcut)
    {
        if (shortcut is not null)
        {
            _shortcuts?.Remove(shortcut);
        }
    }

    private void CloseEditor()
    {
        IsEditorOpen = false;
        EditingShortcut = null;
        EditorError = null;
    }
}
