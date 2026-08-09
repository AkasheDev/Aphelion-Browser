using CommunityToolkit.Mvvm.ComponentModel;

namespace Aphelion.Desktop.UI.ViewModels;

public sealed partial class SplashViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "Starting…";

    /// <summary>Progress from 0 to 100, matching the progress bar's range.</summary>
    [ObservableProperty]
    private double _progressPercent;
}
