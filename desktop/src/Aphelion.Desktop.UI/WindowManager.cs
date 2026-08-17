using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Infrastructure.Storage;
using Aphelion.Desktop.Domain.ValueObjects;
using Aphelion.Desktop.UI.ViewModels;
using Aphelion.Desktop.UI.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace Aphelion.Desktop.UI;

/// <summary>
/// Tracks the open browser windows and moves tabs between them.
/// </summary>
/// <remarks>
/// Tearing a tab out reloads its page. <c>NativeWebView</c> is a native control
/// owned by the window that hosts it, so a tab cannot carry its engine session
/// across; the new window navigates to the same address instead. Scroll position
/// and unsubmitted form state are lost. Chrome avoids this with a process per
/// renderer, which this application does not have.
/// </remarks>
internal sealed class WindowManager
{
    private readonly IServiceProvider _services;
    private readonly ISessionStore _sessionStore;
    private readonly List<MainWindow> _windows = [];
    private bool _persisting;

    public WindowManager(IServiceProvider services, ISessionStore sessionStore)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
    }

    public IReadOnlyList<MainWindow> Windows => _windows;

    public MainWindow CreateWindow(PageAddress? initialAddress = null)
    {
        // A torn-off window shares the favicon cache but not the session store:
        // only the main window's tabs are restored on next launch.
        var shell = new ShellViewModel(
            _services.GetRequiredService<Func<BrowserViewModel>>(),
            this,
            sessionStore: null,
            favicons: _services.GetService<IFaviconLoader>(),
            tabSounds: null,
            bookmarks: _services.GetService<BookmarksViewModel>(),
            downloads: _services.GetService<DownloadsViewModel>(),
            settings: _services.GetService<SettingsViewModel>(),
            history: _services.GetService<HistoryViewModel>(),
            appSettings: _services.GetService<IAppSettingsStore>(),
            restoreOnStart: false);

        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel(shell),
        };

        Register(window);

        if (initialAddress is not null)
        {
            shell.NavigateActiveTab(initialAddress);
        }

        return window;
    }

    /// <summary>
    /// Opens a private window with its own engine profile directory so cookies
    /// and site data never share a store with ordinary windows. The directory is
    /// deleted when the window closes. Session restore is withheld.
    /// </summary>
    public MainWindow CreatePrivateWindow(PageAddress? initialAddress = null)
    {
        var privateAmbient = new NewTabAmbientViewModel(
            _services.GetRequiredService<Aphelion.Desktop.Application.Ports.ICurrentWeatherProvider>(),
            _services.GetRequiredService<Aphelion.Desktop.Application.Ports.IPrivacyPreferenceStore>(),
            isPrivateInstance: true);

        var privateDownloads = new DownloadsViewModel(
            store: null,
            files: _services.GetService<IFileExplorer>());

        var location = _services.GetRequiredService<IUserDataLocation>();
        var privateDirectory = Path.Combine(location.RootDirectory, "private", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(privateDirectory);

        var factory = () => ActivatorUtilities.CreateInstance<BrowserViewModel>(
            _services,
            privateAmbient,
            true,
            privateDownloads);
        var shell = new ShellViewModel(
            factory,
            this,
            sessionStore: null,
            favicons: _services.GetService<IFaviconLoader>(),
            tabSounds: null,
            bookmarks: _services.GetService<BookmarksViewModel>(),
            isPrivateWindow: true,
            downloads: privateDownloads,
            settings: _services.GetService<SettingsViewModel>(),
            history: new HistoryViewModel(new MemoryHistoryStore()) { IsPrivateSession = true },
            isolatedUserDataDirectory: privateDirectory,
            restoreOnStart: false);

        var window = new MainWindow { DataContext = new MainWindowViewModel(shell), Title = "Private Mode — Aphelion" };

        // Merged into this window rather than swapped application-wide: a private
        // window darkens itself while the ordinary windows beside it carry on
        // unchanged. Window resources are found before the application's, so this
        // wins for everything drawn inside it.
        window.Resources.MergedDictionaries.Add(
            (ResourceDictionary)AvaloniaXamlLoader.Load(
                new Uri("avares://Aphelion.Desktop.UI/Themes/PrivatePalette.axaml")));

        Register(window);

        if (initialAddress is not null)
        {
            shell.NavigateActiveTab(initialAddress);
        }

        window.Closing += (_, _) =>
        {
            foreach (var browser in shell.Browsers.ToList())
            {
                _ = browser.ClearPrivateBrowsingDataAsync();
            }

            try
            {
                if (Directory.Exists(privateDirectory))
                {
                    Directory.Delete(privateDirectory, recursive: true);
                }
            }
            catch (Exception)
            {
                // Best effort: leftover private profiles are unused on the next run.
            }
        };

        return window;
    }

    public void Register(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_windows.Contains(window))
        {
            return;
        }

        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
    }

    public void ActivateShell(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        var window = _windows.FirstOrDefault(candidate =>
            candidate.DataContext is MainWindowViewModel { Shell: { } candidateShell } &&
            ReferenceEquals(candidateShell, shell));

        if (window is null)
        {
            return;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
    }

    /// <summary>
    /// Opens a torn-off tab in its own window, positioned under the pointer.
    /// A tab taken from a private window opens in a new private window, so
    /// dragging it out cannot drop it into ordinary browsing.
    /// </summary>
    public void TearOff(TabTransferSnapshot transfer, PixelPoint screenPosition, Size size)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        var initialAddress = transfer.InternalPage is not null || transfer.IsDownloadsPage
            ? null
            : transfer.PrimaryAddress;
        var window = transfer.IsPrivate
            ? CreatePrivateWindow(initialAddress)
            : CreateWindow(initialAddress);

        if (window.DataContext is MainWindowViewModel { Shell: { } shell })
        {
            if (transfer.InternalPage is { } page)
            {
                shell.ActiveBrowser?.ShowInternal(page, recordHistory: false);
            }
            else if (transfer.IsDownloadsPage)
            {
                shell.ActiveBrowser?.ShowDownloads(recordHistory: false);
            }
            if (transfer.PartnerAddress is not null)
            {
                shell.SplitActiveWithAddress(transfer.PartnerAddress);
            }
        }

        window.Width = Math.Max(window.MinWidth, size.Width);
        window.Height = Math.Max(window.MinHeight, size.Height);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = screenPosition;

        window.Show();
    }

    /// <summary>
    /// Shows the drop indicator on whichever window's strip the pointer is over,
    /// and hides it everywhere else. Called continuously during a drag so the user
    /// sees where a cross-window drop would land before releasing.
    /// </summary>
    /// <param name="exclude">
    /// A window that cannot accept the drop. The strip passes its own window,
    /// having already placed the tab live; a drag out of the overflow list passes
    /// nothing, since dropping onto its own strip is the point.
    /// </param>
    /// <param name="sourceIsPrivate">
    /// Privacy of the window the tab is leaving. Private and ordinary strips do
    /// not accept each other's tabs, so the indicator only lights on a match.
    /// </param>
    public void UpdateDropPreview(PixelPoint screenPoint, MainWindow? exclude, bool sourceIsPrivate)
    {
        var target = WindowAcceptingDropAt(screenPoint, exclude, sourceIsPrivate);

        foreach (var window in _windows)
        {
            if (ReferenceEquals(window, target))
            {
                window.ShowDropIndicatorAt(screenPoint);
            }
            else
            {
                window.HideDropIndicator();
            }
        }
    }

    public void ClearDropPreview()
    {
        foreach (var window in _windows)
        {
            window.HideDropIndicator();
        }
    }

    /// <summary>
    /// The window whose tab strip contains <paramref name="screenPoint"/>, ignoring
    /// <paramref name="exclude"/> and any window whose privacy does not match
    /// <paramref name="sourceIsPrivate"/>. Used to decide whether a released drag
    /// lands on another window rather than on empty desktop.
    /// </summary>
    public MainWindow? WindowAcceptingDropAt(
        PixelPoint screenPoint,
        MainWindow? exclude,
        bool sourceIsPrivate)
    {
        foreach (var window in _windows)
        {
            if (ReferenceEquals(window, exclude) || !window.IsVisible)
            {
                continue;
            }

            if (WindowIsPrivate(window) != sourceIsPrivate)
            {
                continue;
            }

            if (window.TabStripContainsScreenPoint(screenPoint))
            {
                return window;
            }
        }

        return null;
    }

    public void PersistAll()
    {
        if (_persisting)
        {
            return;
        }

        _persisting = true;
        try
        {
            var windows = new List<SessionWindowSnapshot>();

            foreach (var window in _windows)
            {
                if (window.DataContext is not MainWindowViewModel { Shell: { } shell } ||
                    shell.IsPrivateWindow)
                {
                    continue;
                }

                windows.Add(shell.CaptureWindowSnapshot(
                    window.Position.X,
                    window.Position.Y,
                    window.Width,
                    window.Height));
            }

            if (windows.Count == 0)
            {
                return;
            }

            _sessionStore.Save(new SessionSnapshot(windows[0].Tabs, windows[0].ActiveIndex, windows));
        }
        finally
        {
            _persisting = false;
        }
    }

    public void RestoreExtraWindows()
    {
        if (_sessionStore.Load() is not { Windows.Count: > 1 } snapshot)
        {
            return;
        }

        for (var i = 1; i < snapshot.Windows.Count; i++)
        {
            var saved = snapshot.Windows[i];
            var window = CreateWindow();
            if (window.DataContext is MainWindowViewModel { Shell: { } shell })
            {
                shell.ReplaceFromSnapshot(saved);
            }

            if (saved.Width > 0 && saved.Height > 0)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = new PixelPoint(saved.Left, saved.Top);
                window.Width = saved.Width;
                window.Height = saved.Height;
            }

            window.Show();
        }
    }

    private static bool WindowIsPrivate(MainWindow window) =>
        window.DataContext is MainWindowViewModel { Shell.IsPrivateWindow: true };
}
