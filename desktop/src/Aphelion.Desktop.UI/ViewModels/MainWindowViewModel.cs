using CommunityToolkit.Mvvm.ComponentModel;

namespace Aphelion.Desktop.UI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(BrowserViewModel browser)
    {
        Browser = browser ?? throw new ArgumentNullException(nameof(browser));
    }

    public BrowserViewModel Browser { get; }

    /// <summary>
    /// Window title. Fixed for now; it becomes the active tab's title once tabs
    /// exist, which is why it is an observable property rather than a constant.
    /// </summary>
    [ObservableProperty]
    private string _title = "Aphelion";
}
