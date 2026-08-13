using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Aphelion.Desktop.UI.ViewModels;

namespace Aphelion.Desktop.UI.Views;

public partial class NewTabPage : UserControl
{
    public NewTabPage()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (IsVisible)
        {
            FocusSearch();
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != IsVisibleProperty || VisualRoot is null)
        {
            return;
        }

        if (change.GetNewValue<bool>())
        {
            FocusSearch();

            // SearchBox may already have been the focused element before this
            // page went invisible (switching tabs does not itself move focus
            // away), in which case SearchBox.Focus() below is a no-op and
            // never raises GotFocus — so the request that OnSearchBoxGotFocus
            // would normally make has to be issued directly here too.
            if (DataContext is NewTabPageViewModel { Search: { } visibleSearch })
            {
                visibleSearch.RequestSuggestionsForFocus();
            }
        }
        else if (DataContext is NewTabPageViewModel { Search: { } search })
        {
            // This page is hidden, not destroyed, whenever the tab it belongs to
            // stops being the visible one — switching tabs while the New Tab page
            // is showing does not tear it down. The suggestions Popup is its own
            // top-level window at the OS level, though, and does not know to
            // close just because the page that opened it went out of view; left
            // alone it stayed floating over whichever tab the user switched to.
            search.ClearSuggestionsForHidden();
        }
    }

    private void FocusSearch()
    {
        Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Loaded);
    }

    private void OnOpenShortcut(object? sender, RoutedEventArgs e)
        => ExecuteShortcutCommand(sender, static (viewModel, shortcut) =>
            viewModel.OpenShortcutCommand.Execute(shortcut));

    private void OnRenameShortcut(object? sender, RoutedEventArgs e)
        => ExecuteShortcutCommand(sender, static (viewModel, shortcut) =>
            viewModel.BeginRenameShortcutCommand.Execute(shortcut));

    private void OnRemoveShortcut(object? sender, RoutedEventArgs e)
        => ExecuteShortcutCommand(sender, static (viewModel, shortcut) =>
            viewModel.RemoveShortcutCommand.Execute(shortcut));

    private void OnAddShortcut(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewTabPageViewModel viewModel)
        {
            viewModel.BeginAddShortcutCommand.Execute(null);
        }
    }

    private void OnSearchBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewTabPageViewModel { Search: { } search })
        {
            search.RequestSuggestionsForFocus();
        }
    }

    private void OnSuggestionsPopupClosed(object? sender, EventArgs e)
    {
        // Light dismiss (clicking anywhere outside the popup) closes it at the
        // Avalonia level without touching Search.HasSuggestions, which is what
        // IsOpen is actually bound to. Left alone, the suggestion list stayed
        // populated behind a closed popup, so clicking back into SearchBox with
        // the same text produced no GotFocus-driven change and the popup never
        // reopened. Clearing here keeps view-model state truthful to what is
        // visible, the same way ClearSuggestionsForHidden does for tab switches.
        //
        // This event fires from inside Popup's own IsOpen change handling —
        // clearing Suggestions synchronously here re-enters the ItemsControl's
        // in-progress CollectionChanged handling for the same close and throws
        // ObservableCollection's reentrancy guard. Posting it lets that call
        // stack unwind first.
        //
        // The post is also why the clear has to re-check focus rather than run
        // unconditionally. The dismissing click may itself land in SearchBox,
        // which raises GotFocus and requests suggestions for the text already
        // there; that request would then be wiped by this clear arriving after
        // it. Clicking into the box has to leave suggestions showing, so only
        // a dismissal that left the box unfocused clears them.
        if (DataContext is NewTabPageViewModel { Search: { } search })
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!SearchBox.IsFocused)
                    {
                        search.ClearSuggestionsForHidden();
                    }
                },
                DispatcherPriority.Background);
        }
    }

    private void OnSearchEngineSelected(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewTabPageViewModel { SearchEngines: { } selector }
            && sender is Control { DataContext: SearchEngineOptionViewModel option })
        {
            selector.SelectCommand.Execute(option);
        }
    }

    private void OnSearchSuggestionSelected(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewTabPageViewModel { Search: { } search }
            && sender is Control { DataContext: string suggestion })
        {
            search.UseSuggestionCommand.Execute(suggestion);
        }
    }

    private void ExecuteShortcutCommand(
        object? sender,
        Action<NewTabPageViewModel, NewTabShortcutViewModel> execute)
    {
        if (DataContext is NewTabPageViewModel viewModel
            && sender is Control { DataContext: NewTabShortcutViewModel shortcut })
        {
            execute(viewModel, shortcut);
        }
    }

}
