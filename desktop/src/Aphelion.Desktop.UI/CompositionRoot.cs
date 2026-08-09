using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Application.UseCases;
using Aphelion.Desktop.Infrastructure.Network;
using Aphelion.Desktop.Infrastructure.Storage;
using Aphelion.Desktop.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Aphelion.Desktop.UI;

/// <summary>
/// Single place where concrete implementations are bound to the ports declared by
/// the inner layers.
/// </summary>
/// <remarks>
/// <see cref="IBrowserEngineSession"/> is deliberately absent: an engine session
/// wraps a live native web view, so it is created by the view that owns that view
/// and handed to the view model. See ADR-0001.
/// </remarks>
internal static class CompositionRoot
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        // Infrastructure
        services.AddSingleton<ISearchQueryBuilder, DuckDuckGoSearchQueryBuilder>();
        services.AddSingleton<IUserDataLocation, UserDataLocation>();

        // Startup sequence. Tasks run in registration order; loading history,
        // bookmarks and settings will be added here as they gain persistence.
        services.AddSingleton<IStartupTask, PrepareUserDataStartupTask>();
        services.AddSingleton<RunStartupSequence>();

        // Application
        services.AddSingleton<NavigateFromAddressBar>();

        // Presentation. Browsers are transient because every tab needs its own
        // engine session and history; the shell creates one per tab.
        services.AddTransient<BrowserViewModel>();
        services.AddSingleton<Func<BrowserViewModel>>(sp => sp.GetRequiredService<BrowserViewModel>);
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<SplashViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
