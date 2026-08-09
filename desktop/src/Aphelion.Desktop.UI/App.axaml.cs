using Aphelion.Desktop.Application.UseCases;
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
        var splash = new SplashWindow { DataContext = splashViewModel };
        splash.Show();

        try
        {
            var progress = new Progress<StartupProgress>(report =>
            {
                splashViewModel.StatusText = report.Description;
                splashViewModel.ProgressPercent = report.Fraction * 100d;
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

        // Let the finished progress bar register before the window swaps.
        await Task.Delay(TimeSpan.FromMilliseconds(350));

        var mainWindow = new MainWindow
        {
            DataContext = services.GetRequiredService<MainWindowViewModel>(),
        };

        desktop.MainWindow = mainWindow;
        desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;

        mainWindow.Show();
        splash.Close();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        Dispatcher.UIThread.VerifyAccess();

        _services?.Dispose();
        _services = null;
    }
}
