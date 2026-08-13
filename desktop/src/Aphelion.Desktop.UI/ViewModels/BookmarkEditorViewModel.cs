using System.Collections.ObjectModel;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>One folder in the editor's destination list, with its depth.</summary>
/// <remarks>
/// The picker is a flat, indented list rather than the cascading popups the bar
/// itself uses. Choosing where something goes and browsing what is already
/// saved are different tasks: the first wants every destination visible at once,
/// which is also the shape Chrome's own "bookmark added" popup takes.
/// </remarks>
public sealed class BookmarkFolderRowViewModel(BookmarkFolder folder, int depth)
{
    public BookmarkFolder Folder { get; } = folder ?? throw new ArgumentNullException(nameof(folder));

    public string Name => Folder.Name;

    /// <summary>Left inset in pixels, one step per level.</summary>
    public Avalonia.Thickness Indent { get; } = new(depth * 16, 0, 0, 0);
}

/// <summary>
/// The form behind the star button: name the page and choose where it goes.
/// Exists only while the popup is open.
/// </summary>
public sealed partial class BookmarkEditorViewModel : ViewModelBase
{
    private readonly BookmarksViewModel _bookmarks;
    private readonly Bookmark? _editing;
    private readonly PageAddress _address;
    private readonly PageAddress? _faviconAddress;

    public BookmarkEditorViewModel(
        BookmarksViewModel bookmarks,
        PageAddress address,
        string suggestedName,
        PageAddress? faviconAddress = null,
        Bookmark? editing = null)
    {
        _bookmarks = bookmarks ?? throw new ArgumentNullException(nameof(bookmarks));
        _address = address ?? throw new ArgumentNullException(nameof(address));
        _faviconAddress = faviconAddress;
        _editing = editing;

        _name = editing?.Name ?? suggestedName;

        var tree = bookmarks.Tree;
        Folders = [new BookmarkFolderRowViewModel(tree.Root, 0)];
        AddFolderRows(tree.Root, 1);

        var parent = editing is null ? tree.Root : tree.ParentOf(editing.Id) ?? tree.Root;
        _selectedFolder = Folders.FirstOrDefault(row => ReferenceEquals(row.Folder, parent)) ?? Folders[0];
    }

    /// <summary>Every folder the bookmark could go into, depth first.</summary>
    public ObservableCollection<BookmarkFolderRowViewModel> Folders { get; }

    /// <summary>True when the star was clicked on a page that is already saved.</summary>
    public bool IsEditing => _editing is not null;

    public string Heading => IsEditing ? "Edit bookmark" : "Bookmark added";

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private BookmarkFolderRowViewModel _selectedFolder;

    /// <summary>Raised when the form is finished with, so the shell can close it.</summary>
    public event EventHandler? Closed;

    [RelayCommand]
    private void Save()
    {
        var destination = SelectedFolder.Folder;

        if (_editing is null)
        {
            _bookmarks.AddBookmark(destination, Name, _address, _faviconAddress);
            Close();
            return;
        }

        _bookmarks.Rename(_editing.Id, Name);

        if (!ReferenceEquals(_bookmarks.Tree.ParentOf(_editing.Id), destination))
        {
            _bookmarks.Move(_editing.Id, destination);
        }

        Close();
    }

    /// <summary>
    /// Discards an edit. A bookmark that was just added by the star stays added:
    /// the star saved it before the form opened, matching Chrome, where this
    /// button dismisses the form rather than undoing the bookmark.
    /// </summary>
    [RelayCommand]
    private void Cancel() => Close();

    [RelayCommand]
    private void Remove()
    {
        if (_editing is not null)
        {
            _bookmarks.Remove(_editing.Id);
        }

        Close();
    }

    private void Close() => Closed?.Invoke(this, EventArgs.Empty);

    private void AddFolderRows(BookmarkFolder parent, int depth)
    {
        foreach (var folder in parent.Children.OfType<BookmarkFolder>())
        {
            Folders.Add(new BookmarkFolderRowViewModel(folder, depth));
            AddFolderRows(folder, depth + 1);
        }
    }
}
