using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>Ctrl+K command palette for the window.</summary>
public sealed partial class CommandPaletteViewModel : ViewModelBase
{
    private readonly IReadOnlyList<CommandPaletteItem> _all;

    public CommandPaletteViewModel(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _all =
        [
            new("New tab", "Ctrl+T", shell.NewTabCommand),
            new("Settings", null, shell.OpenSettingsPageCommand),
            new("History", "Ctrl+H", shell.OpenHistoryPageCommand),
            new("Downloads", "Ctrl+J", shell.OpenDownloadsPageCommand),
            new("Find in page", "Ctrl+F", shell.FindInPageCommand),
            new("Reload", "Ctrl+R", shell.ReloadActiveCommand),
            new("Print", "Ctrl+P", shell.PrintActiveCommand),
            new("Developer tools", "F12", shell.OpenDevToolsCommand),
            new("Reopen closed tab", "Ctrl+Shift+T", shell.ReopenClosedTabCommand),
            new("Command palette", "Ctrl+K", shell.ToggleCommandPaletteCommand),
        ];
        Rebuild();
    }

    public ObservableCollection<CommandPaletteItem> Matches { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private CommandPaletteItem? _selected;

    partial void OnQueryChanged(string value) => Rebuild();

    [RelayCommand]
    private void Run(CommandPaletteItem? item)
    {
        (item ?? Selected)?.Command.Execute(null);
        Query = string.Empty;
    }

    private void Rebuild()
    {
        Matches.Clear();
        var text = Query.Trim();
        foreach (var item in _all)
        {
            if (text.Length == 0 ||
                item.Title.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                Matches.Add(item);
            }
        }

        Selected = Matches.FirstOrDefault();
    }
}

public sealed record CommandPaletteItem(
    string Title,
    string? Shortcut,
    System.Windows.Input.ICommand Command);
