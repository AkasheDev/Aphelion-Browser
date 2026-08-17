using Aphelion.Desktop.UI.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Aphelion.Desktop.UI.Views;

public partial class BrowserToolbarView : UserControl
{
    public BrowserToolbarView() => InitializeComponent();

    /// <summary>
    /// Clears the editor when its popup is light-dismissed, so the shell's state
    /// matches what is on screen and the star can open it again.
    /// </summary>
    private void OnBookmarkEditorClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel { Shell: { } shell })
        {
            Dispatcher.UIThread.Post(
                () => shell.CloseBookmarkEditorCommand.Execute(null),
                DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Posted after the click that dismissed the popup, so toggling the
    /// downloads button closed does not immediately reopen it.
    /// </summary>
    private void OnDownloadsClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel { Shell: { } shell })
        {
            Dispatcher.UIThread.Post(
                () => shell.CloseDownloadsBubbleCommand.Execute(null),
                DispatcherPriority.Background);
        }
    }

    /// <summary>Focuses and selects the shared address input for Ctrl+L.</summary>
    public void FocusAddress() =>
        Dispatcher.UIThread.Post(
            () =>
            {
                AddressBox.Focus();
                AddressBox.SelectAll();
            },
            DispatcherPriority.Input);

    private void OnAddressGotFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { Shell.FocusedBrowser: { } browser })
        {
            browser.IsAddressEditing = true;
            _ = browser.RefreshAddressSuggestionsAsync();
        }
    }

    private void OnAddressLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { Shell.FocusedBrowser: { } browser })
        {
            Dispatcher.UIThread.Post(
                () => browser.IsAddressEditing = false,
                DispatcherPriority.Background);
        }
    }
}
