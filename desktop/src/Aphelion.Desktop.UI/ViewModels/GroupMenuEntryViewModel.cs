using System.Windows.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// One row of a saved group's context menu: an action, a separator, the heading
/// above the group's pages, or one of those pages.
/// </summary>
/// <remarks>
/// The menu lists the group's pages under its actions, the way Chrome's does, and
/// a menu cannot mix items declared in markup with items bound from a collection.
/// So the whole menu is built as one list and this is what its rows are made of.
/// A page row keeps hold of the row view model it came from rather than copying
/// its title and icon, since the icon arrives later than the menu does.
/// </remarks>
public sealed class GroupMenuEntryViewModel
{
    /// <summary>Avalonia draws a menu item whose header is "-" as a separator.</summary>
    private const string SeparatorLabel = "-";

    private GroupMenuEntryViewModel(
        string label,
        ICommand? command = null,
        object? commandParameter = null,
        BookmarkNodeViewModel? page = null,
        bool isHeading = false)
    {
        Label = label;
        Command = command;
        CommandParameter = commandParameter;
        Page = page;
        IsHeading = isHeading;
    }

    public string Label { get; }

    public ICommand? Command { get; }

    public object? CommandParameter { get; }

    /// <summary>The page this row stands for, when it is one. Carries the favicon.</summary>
    public BookmarkNodeViewModel? Page { get; }

    public bool IsHeading { get; }

    /// <summary>A heading labels the list below it and is not itself a choice.</summary>
    public bool IsEnabled => !IsHeading && Command is not null;

    public static GroupMenuEntryViewModel Action(
        string label,
        ICommand? command,
        object? commandParameter = null) =>
        new(label, command, commandParameter);

    public static GroupMenuEntryViewModel Separator() => new(SeparatorLabel);

    public static GroupMenuEntryViewModel Heading(string label) => new(label, isHeading: true);

    public static GroupMenuEntryViewModel ForPage(
        BookmarkNodeViewModel page,
        ICommand? command,
        object? commandParameter) =>
        new(page.Name, command, commandParameter, page);
}
