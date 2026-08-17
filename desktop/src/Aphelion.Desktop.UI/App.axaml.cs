using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Application.UseCases;
using Aphelion.Desktop.UI.Services;
using Aphelion.Desktop.UI.ViewModels;
using Aphelion.Desktop.UI.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Aphelion.Desktop.UI;

public partial class App : Avalonia.Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = CompositionRoot.Build();

            // The main window opens only after startup work finishes, so shutting
            // down with the last window would quit during the splash.
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            desktop.ShutdownRequested += OnShutdownRequested;

            StartAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void StartAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var services = _services!;

        var splashViewModel = services.GetRequiredService<SplashViewModel>();
        var sounds = services.GetRequiredService<ITabSoundPlayer>();
        splashViewModel.OrbitDuration = sounds.SplashDuration;
        if (Environment.GetCommandLineArgs().Contains("--splash"))
        {
            splashViewModel.LoopOrbit = true;
        }

        var splash = new SplashWindow { DataContext = splashViewModel };

        // The window is on screen at Opened, not at Show: Show only asks the
        // platform to present it. Play then, and stop when the splash goes away
        // so the cue never continues under the browser. The hold starts here too,
        // so the orbit and the cue share one clock.
        var cueHold = Task.CompletedTask;
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        splash.Opened += (_, _) =>
        {
            sounds.PlaySplash(splashViewModel.LoopOrbit);
            cueHold = Task.Delay(sounds.SplashDuration);
            opened.TrySetResult();
        };
        splash.Closed += (_, _) => sounds.StopSplash();
        splash.Show();
        await opened.Task;

        // Started with --splash, the splash is the whole application: a looping
        // lap timed to the cue so its orbit can actually be watched.
        if (Environment.GetCommandLineArgs().Contains("--splash"))
        {
            await PreviewSplashAsync(splashViewModel, splash);
            return;
        }

        try
        {
            var progress = new Progress<StartupProgress>(report =>
            {
                splashViewModel.StatusText = report.Description;
            });

            await services.GetRequiredService<RunStartupSequence>()
                .ExecuteAsync(progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Startup failed. Report it on the splash rather than vanishing, then
            // stop: opening a browser whose profile did not load would silently
            // lose the user's data.
            splashViewModel.StatusText = $"Startup failed: {ex.Message}";
            await Task.Delay(TimeSpan.FromSeconds(6));
            splash.Close();
            desktop.Shutdown(1);
            return;
        }

        // The cue is the floor: if loading already finished, the body still has
        // to complete its lap. If the cue already ended, loading continues and
        // this returns at once — the browser opens when the work is done.
        await cueHold;

        // A beat at home so the arrival is seen before the window swaps.
        await Task.Delay(TimeSpan.FromMilliseconds(350));

        var settings = services.GetRequiredService<IAppSettingsStore>();
        ThemeController.Apply(settings);

        if (!settings.Load().HasCompletedOnboarding)
        {
            var onboardingClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var onboarding = new OnboardingWindow
            {
                DataContext = new OnboardingViewModel(
                    settings,
                    services.GetRequiredService<ISearchEnginePreference>(),
                    () => onboardingClosed.TrySetResult()),
            };
            onboarding.Closed += (_, _) => onboardingClosed.TrySetResult();
            onboarding.Show();
            await onboardingClosed.Task;
            onboarding.Close();
            ThemeController.Apply(settings);
        }

        var mainWindow = new MainWindow
        {
            DataContext = services.GetRequiredService<MainWindowViewModel>(),
        };

        // Registered so tabs torn off this window can find their way back.
        var windowManager = services.GetRequiredService<WindowManager>();
        windowManager.Register(mainWindow);

        desktop.MainWindow = mainWindow;

        // Every browser window is equal once it exists: tearing a tab or a group
        // out makes a window that outlives the one it came from, so the browser
        // closes with its last window rather than with this first one. Tying
        // shutdown to the main window took the other windows down with it, losing
        // whatever was open in them.
        desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose;

        mainWindow.Show();
        windowManager.RestoreExtraWindows();
        splash.Close();
    }

    /// <summary>
    /// Holds the splash open until it is closed. The orbit itself is timed to the
    /// cue and loops for this preview; this method only keeps the window alive.
    /// </summary>
    private static async Task PreviewSplashAsync(SplashViewModel model, SplashWindow splash)
    {
        var closed = false;
        splash.Closed += (_, _) => closed = true;
        model.StatusText = "Splash preview";

        while (!closed)
        {
            await Task.Delay(200);
        }
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        Dispatcher.UIThread.VerifyAccess();

        SaveSession();

        _services?.Dispose();
        _services = null;
    }

    /// <summary>Persists the open tabs so the next run reopens where the user left off.</summary>
    private void SaveSession()
    {
        try
        {
            if (_services is not { } services)
            {
                return;
            }

            if (services.GetService<WindowManager>() is { } manager)
            {
                manager.PersistAll();
            }
            else if (services.GetService<ViewModels.ShellViewModel>() is { } shell &&
                services.GetService<Aphelion.Desktop.Application.Ports.ISessionStore>() is { } store)
            {
                store.Save(shell.CaptureSnapshot());
            }
        }
        catch (Exception)
        {
            // Failing to persist the session must not block shutdown.
        }
    }
}
