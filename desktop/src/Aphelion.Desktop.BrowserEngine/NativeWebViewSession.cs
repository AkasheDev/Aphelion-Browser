using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.ValueObjects;
using Avalonia.Controls;

namespace Aphelion.Desktop.BrowserEngine;

/// <summary>
/// Implements <see cref="IBrowserEngineSession"/> over Avalonia's
/// <see cref="NativeWebView"/>, which renders through the host platform's own
/// engine: WebView2 on Windows, WKWebView on macOS, WPE WebKit on Linux.
/// </summary>
/// <remarks>
/// This adapter is the only place in the desktop application that references the
/// web view type. Replacing the engine — see ADR-0001 — means writing a sibling of
/// this class and changing one registration in the composition root.
/// </remarks>
public sealed class NativeWebViewSession : IBrowserEngineSession, IDisposable
{
    private readonly NativeWebView _webView;
    private bool _disposed;

    public NativeWebViewSession(NativeWebView webView)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));

        _webView.NavigationStarted += OnNavigationStarted;
        _webView.NavigationCompleted += OnNavigationCompleted;
    }

    public bool CanGoBack => _webView.CanGoBack;

    public bool CanGoForward => _webView.CanGoForward;

    public event EventHandler<EngineNavigationStartedEventArgs>? NavigationStarted;

    public event EventHandler<EngineNavigationCompletedEventArgs>? NavigationCompleted;

    public void Navigate(PageAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        _webView.Navigate(address.Value);
    }

    public bool GoBack() => _webView.GoBack();

    public bool GoForward() => _webView.GoForward();

    public bool Reload() => _webView.Refresh();

    public bool StopLoading() => _webView.Stop();

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e) =>
        NavigationStarted?.Invoke(this, new EngineNavigationStartedEventArgs(e.Request));

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e) =>
        NavigationCompleted?.Invoke(
            this,
            new EngineNavigationCompletedEventArgs(e.IsSuccess, e.IsSuccess ? null : "Navigation failed."));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _webView.NavigationStarted -= OnNavigationStarted;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _disposed = true;
    }
}
