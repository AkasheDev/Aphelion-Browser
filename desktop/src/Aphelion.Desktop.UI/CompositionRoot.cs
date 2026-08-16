using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Application.UseCases;
using Aphelion.Desktop.Infrastructure.Network;
using Aphelion.Desktop.Infrastructure.Storage;
using Aphelion.Desktop.UI.ViewModels;
using Aphelion.Desktop.UI.Services;
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
        services.AddSingleton<IBookmarkStore, JsonBookmarkStore>();
        services.AddSingleton<INewTabShortcutStore, JsonNewTabShortcutStore>();
        services.AddSingleton<IFaviconLoader, FaviconLoader>();
        services.AddSingleton<ICurrentWeatherProvider, OpenMeteoCurrentWeatherProvider>();
        services.AddSingleton<ISearchSuggestionProvider, SearchEngineSuggestionProvider>();
        services.AddSingleton<ISearchEnginePreferenceStore, JsonSearchEnginePreferenceStore>();
        services.AddSingleton<ISiteZoomStore, JsonSiteZoomStore>();
        services.AddSingleton<IPrivacyPreferenceStore, JsonPrivacyPreferenceStore>();
        services.AddSingleton<IDownloadHistoryStore, JsonDownloadHistoryStore>();
        services.AddSingleton<IFileExplorer, SystemFileExplorer>();
        // Constructed eagerly here, rather than registered by type, because its
        // native engine is only bundled for Windows and macOS (see the type's
        // remarks) — a platform without it must fall back to silence instead of
        // failing to start.
        services.AddSingleton<ITabSoundPlayer>(_ => CreateSoundPlayer());
        services.AddSingleton<ConfigurableSearchQueryBuilder>();
        services.AddSingleton<ISearchQueryBuilder>(sp =>
            sp.GetRequiredService<ConfigurableSearchQueryBuilder>());
        services.AddSingleton<ISearchEnginePreference>(sp =>
            sp.GetRequiredService<ConfigurableSearchQueryBuilder>());

        // Startup sequence. Tasks run in registration order; loading history and
        // settings will be added here as they gain persistence.
        services.AddSingleton<IStartupTask, PrepareUserDataStartupTask>();
        services.AddSingleton<RunStartupSequence>();

        // Application
        services.AddSingleton<NavigateFromAddressBar>();
        services.AddSingleton<ManageSiteZoom>();
        services.AddSingleton<ManageNewTabShortcuts>();
        services.AddSingleton<NewTabShortcutHub>();
        services.AddSingleton<NewTabAmbientViewModel>();
        services.AddSingleton<SearchEngineSelectorViewModel>();

        // Bookmarks belong to the profile rather than to a window, so torn-off
        // windows share one tree and one bar view model between them.
        services.AddSingleton(sp =>
            BookmarksViewModel.Restore(sp.GetRequiredService<IBookmarkStore>()));
        services.AddSingleton<BookmarksViewModel>();

        // Downloads belong to the profile the same way bookmarks do: a file
        // started in one window is this user's download in every other.
        services.AddSingleton<DownloadsViewModel>();

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
            sp.GetRequiredService<IFaviconLoader>(),
            sp.GetRequiredService<ITabSoundPlayer>(),
            sp.GetRequiredService<BookmarksViewModel>(),
            downloads: sp.GetRequiredService<DownloadsViewModel>()));
        services.AddSingleton<SplashViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

    private static ITabSoundPlayer CreateSoundPlayer()
    {
        try
        {
            return new LibVlcTabSoundPlayer();
        }
        catch (Exception)
        {
            // No bundled native LibVLC for this platform, or it failed to load.
            // Interaction sounds are a nicety; the browser must still start.
            return new NullTabSoundPlayer();
        }
    }
}
