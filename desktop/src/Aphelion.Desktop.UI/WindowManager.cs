using Aphelion.Desktop.Domain.ValueObjects;
using Aphelion.Desktop.UI.ViewModels;
using Aphelion.Desktop.UI.Views;
using Avalonia;
using Avalonia.Controls;
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
internal sealed class WindowManager(IServiceProvider services)
{
    private readonly IServiceProvider _services =
        services ?? throw new ArgumentNullException(nameof(services));

    private readonly List<MainWindow> _windows = [];

    public IReadOnlyList<MainWindow> Windows => _windows;

    public MainWindow CreateWindow(PageAddress? initialAddress = null)
    {
        // A torn-off window shares the favicon cache but not the session store:
        // only the main window's tabs are restored on next launch.
        var shell = new ShellViewModel(
            _services.GetRequiredService<Func<BrowserViewModel>>(),
            this,
            sessionStore: null,
            favicons: _services.GetService<Aphelion.Desktop.Application.Ports.IFaviconLoader>());

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

    public MainWindow CreatePrivateWindow()
    {
        var factory = () => ActivatorUtilities.CreateInstance<BrowserViewModel>(_services, true);
        var shell = new ShellViewModel(
            factory,
            this,
            sessionStore: null,
            favicons: _services.GetService<Aphelion.Desktop.Application.Ports.IFaviconLoader>());

        var window = new MainWindow { DataContext = new MainWindowViewModel(shell), Title = "Private Mode — Aphelion" };
        Register(window);
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

    /// <summary>
    /// Opens a torn-off tab in its own window, positioned under the pointer.
    /// </summary>
    public void TearOff(TabTransferSnapshot transfer, PixelPoint screenPosition, Size size)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        var window = CreateWindow(transfer.PrimaryAddress);

        if (window.DataContext is MainWindowViewModel { Shell: { } shell })
        {
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
    public void UpdateDropPreview(PixelPoint screenPoint, MainWindow? exclude)
    {
        var target = WindowAcceptingDropAt(screenPoint, exclude);

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
    /// <paramref name="exclude"/>. Used to decide whether a released drag lands on
    /// another window rather than on empty desktop.
    /// </summary>
    public MainWindow? WindowAcceptingDropAt(PixelPoint screenPoint, MainWindow? exclude)
    {
        foreach (var window in _windows)
        {
            if (ReferenceEquals(window, exclude) || !window.IsVisible)
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
}
