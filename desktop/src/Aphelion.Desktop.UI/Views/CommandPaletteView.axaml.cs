using Avalonia.Controls;
using Avalonia.Input;
using Aphelion.Desktop.UI.ViewModels;

namespace Aphelion.Desktop.UI.Views;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView() => InitializeComponent();

    private void OnRun(object? sender, TappedEventArgs e)
    {
        if (DataContext is CommandPaletteViewModel palette)
        {
            palette.RunCommand.Execute(palette.Selected);
        }
    }
}
