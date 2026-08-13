using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Aphelion.Desktop.UI.Views;

public partial class BookmarkEditorView : UserControl
{
    public BookmarkEditorView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // The name is the field most often changed straight after saving, so it
        // starts focused and selected, ready to be typed over.
        Dispatcher.UIThread.Post(
            () =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            },
            DispatcherPriority.Loaded);
    }
}
