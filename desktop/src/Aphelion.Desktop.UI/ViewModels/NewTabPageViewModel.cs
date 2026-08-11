using System.Collections.ObjectModel;
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
    private readonly Func<string, bool> _search;
    private readonly Action<PageAddress> _navigate;

    public NewTabPageViewModel(
        NewTabShortcutHub? shortcuts,
        NewTabAmbientViewModel? ambient,
        SearchEngineSelectorViewModel? searchEngines,
        Func<string, bool> search,
        Action<PageAddress> navigate)
    {
        _shortcuts = shortcuts;
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        Ambient = ambient;
        SearchEngines = searchEngines;
    }

    public NewTabAmbientViewModel? Ambient { get; }

    public SearchEngineSelectorViewModel? SearchEngines { get; }

    public ObservableCollection<object> ShortcutTiles => _shortcuts?.Tiles ?? EmptyTiles;

    [ObservableProperty]
    private string _searchText = string.Empty;

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
    private void Search()
    {
        if (_search(SearchText))
        {
            SearchText = string.Empty;
        }
    }

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
