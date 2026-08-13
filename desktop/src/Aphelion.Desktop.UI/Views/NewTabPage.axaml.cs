using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Aphelion.Desktop.UI.ViewModels;

namespace Aphelion.Desktop.UI.Views;

public partial class NewTabPage : UserControl
{
    public NewTabPage()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPagePointerPressed, RoutingStrategies.Tunnel);
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

    /// <summary>
    /// Parks the suggestion list when the search box is left.
    /// </summary>
    /// <remarks>
    /// Deferred rather than run inline: clicking a suggestion moves focus to
    /// that row's button, which raises this before the click is delivered.
    /// Hiding the list there would remove the button out from under the press.
    /// Posting the check lets focus settle first, and a focus that landed inside
    /// the list is not a departure at all.
    /// </remarks>
    private void OnSearchBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NewTabPageViewModel { Search: { } search })
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();

                if (focused is Visual visual && IsWithin(visual, SuggestionList))
                {
                    return;
                }

                search.HideSuggestionsForLostFocus();
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Puts the suggestion list away when the pointer goes down anywhere on the
    /// page that is not the search box or the list itself.
    /// </summary>
    /// <remarks>
    /// The list used to live in a Popup, which brought light dismiss with it.
    /// It is now an ordinary part of the page — see the remarks in the XAML for
    /// why — so that behaviour has to be provided here. Handled on the tunnel so
    /// it runs before a click is consumed by whatever it landed on.
    ///
    /// This hides rather than clears, and it is the reason the search box cannot
    /// be relied on to report the departure itself: clicking empty page does not
    /// move focus out of a TextBox, so no LostFocus arrives, and none arrives on
    /// the way back either. Discarding the list here left a box that still held
    /// the query but showed nothing until it was retyped.
    /// </remarks>
    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // No HasSuggestions guard here: while the list is parked that is already
        // false, and this handler is exactly what has to notice the click that
        // brings it back.
        if (DataContext is not NewTabPageViewModel { Search: { } search } ||
            e.Source is not Visual source)
        {
            return;
        }

        if (IsWithin(source, SuggestionList))
        {
            return;
        }

        // Clicking the box is the way back: focus never left it, so GotFocus
        // will not fire to restore what the click before this one put away.
        if (IsWithin(source, SearchBox))
        {
            search.RequestSuggestionsForFocus();
            return;
        }

        search.HideSuggestionsForLostFocus();
    }

    private static bool IsWithin(Visual candidate, Visual container)
    {
        for (var v = candidate; v is not null; v = v.GetVisualParent())
        {
            if (ReferenceEquals(v, container))
            {
                return true;
            }
        }

        return false;
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
