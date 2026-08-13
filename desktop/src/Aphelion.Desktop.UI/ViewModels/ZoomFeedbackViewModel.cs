using Aphelion.Desktop.Domain.ValueObjects;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// Owns one zoom notification for the entire window.
/// </summary>
/// <remarks>
/// Browser tabs can be switched or a different split pane can receive focus
/// while feedback is visible. Keeping its timer and value at window scope makes
/// those changes irrelevant and guarantees that rapid zoom input restarts one
/// deterministic lifetime rather than launching competing asynchronous delays.
/// </remarks>
public sealed partial class ZoomFeedbackViewModel : ViewModelBase
{
    private static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(5);
    private readonly DispatcherTimer _hideTimer;

    public ZoomFeedbackViewModel()
    {
        _hideTimer = new DispatcherTimer { Interval = VisibleDuration };
        _hideTimer.Tick += OnHideTimerTick;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private int _percent = PageZoom.DefaultPercent;

    [ObservableProperty]
    private bool _isVisible;

    public string Label => $"{Percent}%";

    public void Show(int percent)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(percent));
            return;
        }

        Percent = PageZoom.FromPercent(percent).Percent;
        IsVisible = true;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        IsVisible = false;
    }
}
