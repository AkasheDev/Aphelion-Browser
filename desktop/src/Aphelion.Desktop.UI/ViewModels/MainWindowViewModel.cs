using CommunityToolkit.Mvvm.ComponentModel;

namespace Aphelion.Desktop.UI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Aphelion";

    [ObservableProperty]
    private string _status = "Desktop shell is ready. Browser features are not implemented yet.";
}
