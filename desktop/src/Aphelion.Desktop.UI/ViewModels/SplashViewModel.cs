using CommunityToolkit.Mvvm.ComponentModel;

namespace Aphelion.Desktop.UI.ViewModels;

public sealed partial class SplashViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "Starting…";

    /// <summary>
    /// How long the orbiting body takes to complete one lap. Timed to the splash
    /// cue so it arrives home as the sound ends.
    /// </summary>
    public TimeSpan OrbitDuration { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// When set, the body keeps lapping instead of stopping at home. Used by the
    /// --splash preview so the orbit can be watched without a real startup.
    /// </summary>
    public bool LoopOrbit { get; set; }
}
