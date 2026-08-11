namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>Sentinel tile appended after the persisted New Tab launchers.</summary>
public sealed class AddNewTabShortcutViewModel
{
    public static AddNewTabShortcutViewModel Instance { get; } = new();

    private AddNewTabShortcutViewModel()
    {
    }
}
