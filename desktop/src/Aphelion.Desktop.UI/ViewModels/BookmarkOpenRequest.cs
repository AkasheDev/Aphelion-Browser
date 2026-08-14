namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// One entry in a row's open menu: the row, and where it should be opened.
/// </summary>
/// <remarks>
/// Paired for the same reason <see cref="BookmarkMoveTargetViewModel"/> is: a
/// menu item inside a ContextMenu lives in its own popup tree, where binding
/// back to the row that opened the menu is fragile. Passing both halves as one
/// parameter keeps the item bound to nothing but itself.
/// </remarks>
public sealed record BookmarkOpenRequest(BookmarkNodeViewModel Node, BookmarkOpenTarget Target);
