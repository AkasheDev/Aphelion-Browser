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
        services.AddSingleton<IUserDataLocation, UserDataLocation>();
        services.AddSingleton<ISessionStore, JsonSessionStore>();
        services.AddSingleton<INewTabShortcutStore, JsonNewTabShortcutStore>();
        services.AddSingleton<IFaviconLoader, FaviconLoader>();
        services.AddSingleton<ICurrentWeatherProvider, OpenMeteoCurrentWeatherProvider>();
        services.AddSingleton<ISearchSuggestionProvider, SearchEngineSuggestionProvider>();
        services.AddSingleton<ISearchEnginePreferenceStore, JsonSearchEnginePreferenceStore>();
        services.AddSingleton<ConfigurableSearchQueryBuilder>();
        services.AddSingleton<ISearchQueryBuilder>(sp =>
            sp.GetRequiredService<ConfigurableSearchQueryBuilder>());
        services.AddSingleton<ISearchEnginePreference>(sp =>
            sp.GetRequiredService<ConfigurableSearchQueryBuilder>());

        // Startup sequence. Tasks run in registration order; loading history,
        // bookmarks and settings will be added here as they gain persistence.
        services.AddSingleton<IStartupTask, PrepareUserDataStartupTask>();
        services.AddSingleton<RunStartupSequence>();

        // Application
        services.AddSingleton<NavigateFromAddressBar>();
        services.AddSingleton<ManageNewTabShortcuts>();
        services.AddSingleton<NewTabShortcutHub>();
        services.AddSingleton<NewTabAmbientViewModel>();
        services.AddSingleton<SearchEngineSelectorViewModel>();

        // Presentation. Browsers are transient because every tab needs its own
        // engine session and history; the shell creates one per tab.
        services.AddTransient<BrowserViewModel>();
        services.AddSingleton<Func<BrowserViewModel>>(sp => sp.GetRequiredService<BrowserViewModel>);
        // One window manager for the process; each window gets its own shell so
        // tabs can move between windows. Only the main shell restores and saves
        // the session — torn-off windows are transient.
        services.AddSingleton<WindowManager>();
        services.AddSingleton(sp => new ShellViewModel(
            sp.GetRequiredService<Func<BrowserViewModel>>(),
            sp.GetRequiredService<WindowManager>(),
            sp.GetRequiredService<ISessionStore>(),
            sp.GetRequiredService<IFaviconLoader>()));
        services.AddSingleton<SplashViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
