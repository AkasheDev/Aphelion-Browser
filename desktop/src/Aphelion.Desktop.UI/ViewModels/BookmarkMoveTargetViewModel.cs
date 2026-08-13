using Aphelion.Desktop.Domain.Entities;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// One destination in a row's "Move to" menu: a folder, plus the command that
/// files that particular row into it.
/// </summary>
/// <remarks>
/// The pairing is deliberate. A menu item inside a ContextMenu sits in its own
/// popup tree, where binding back to the row that opened the menu is awkward and
/// fragile; carrying both halves in the entry means the item binds to nothing
/// but itself.
/// </remarks>
public sealed partial class BookmarkMoveTargetViewModel(
    BookmarksViewModel owner,
    BookmarkNodeViewModel node,
    BookmarkFolder destination,
    int depth) : ViewModelBase
{
    /// <summary>Indented by depth so nesting is legible in a flat menu.</summary>
    public string Label { get; } = new string(' ', depth * 4) + destination.Name;

    [RelayCommand]
    private void Move() => owner.Move(node.Id, destination);
}
