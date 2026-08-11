using Avalonia.Controls;
using Avalonia.Threading;

namespace Aphelion.Desktop.UI.Views;

public partial class BrowserToolbarView : UserControl
{
    public BrowserToolbarView() => InitializeComponent();

    /// <summary>Focuses and selects the shared address input for Ctrl+L.</summary>
    public void FocusAddress() =>
        Dispatcher.UIThread.Post(
            () =>
            {
                AddressBox.Focus();
                AddressBox.SelectAll();
            },
            DispatcherPriority.Input);

}
