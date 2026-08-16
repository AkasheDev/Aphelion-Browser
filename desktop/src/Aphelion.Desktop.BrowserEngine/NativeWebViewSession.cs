using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.BrowserEngine.Windows;
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
    private readonly WebView2ZoomBridge _windowsZoom;
    private readonly WebView2DownloadBridge _windowsDownloads;
    private bool _disposed;
    private bool _navigationInProgress;
    private Uri? _latestNavigationRequest;
    private double _desiredZoomFactor = 1d;
    private double _actualZoomFactor = 1d;

    public NativeWebViewSession(NativeWebView webView)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _windowsZoom = new WebView2ZoomBridge(OnNativeZoomChanged);
        _windowsDownloads = new WebView2DownloadBridge(OnEngineDownloadStarted);

        _webView.NavigationStarted += OnNavigationStarted;
        _webView.NavigationCompleted += OnNavigationCompleted;
        _webView.AdapterCreated += OnAdapterCreated;
        _webView.AdapterDestroyed += OnAdapterDestroyed;

        TryAttachPlatformBridges(_webView.TryGetPlatformHandle());
    }

    public bool CanGoBack => _webView.CanGoBack;

    public bool CanGoForward => _webView.CanGoForward;

    public event EventHandler<EngineNavigationStartedEventArgs>? NavigationStarted;

    public event EventHandler<EngineNavigationCompletedEventArgs>? NavigationCompleted;

    public event EventHandler<EngineZoomFactorChangedEventArgs>? ZoomFactorChanged;

    public event EventHandler<EngineDownloadStartedEventArgs>? DownloadStarted;

    public void Navigate(PageAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        _webView.Navigate(address.Value);
    }

    public double SetZoomFactor(double factor)
    {
        if (_disposed)
        {
            return _actualZoomFactor;
        }

        _desiredZoomFactor = Math.Clamp(factor, 0.25d, 5d);

        if (_windowsZoom.TrySet(_desiredZoomFactor, out var appliedFactor))
        {
            _actualZoomFactor = Math.Clamp(appliedFactor, 0.25d, 5d);
            _ = RestoreDocumentZoomAsync();
            return _actualZoomFactor;
        }

        _ = ApplyZoomAsync();
        _actualZoomFactor = _desiredZoomFactor;
        return _actualZoomFactor;
    }

    public bool GoBack() => _webView.GoBack();

    public bool GoForward() => _webView.GoForward();

    public bool Reload() => _webView.Refresh();

    public bool StopLoading() => _webView.Stop();

    /// <summary>
    /// Deletes every cookie the underlying adapter reports. This is the closest
    /// this application can come to a private profile: Avalonia's public web view
    /// surface — see the type remarks — exposes a cookie manager but not a
    /// separate storage partition, so "private" here means "cleared on close"
    /// rather than "never written".
    /// </summary>
    public async Task ClearBrowsingDataAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var manager = _webView.TryGetCookieManager();

            if (manager is null)
            {
                return;
            }

            foreach (var cookie in await manager.GetCookiesAsync().ConfigureAwait(true))
            {
                manager.DeleteCookie(cookie.Name, cookie.Domain, cookie.Path);
            }
        }
        catch (Exception)
        {
            // Best effort. A window closing must never be blocked by cookie
            // cleanup, and the adapter's cookie contract is not guaranteed on
            // every platform.
        }
    }

    public async Task<string?> EvaluateAsync(string script)
    {
        if (_disposed)
        {
            return null;
        }

        try
        {
            var result = await _webView.InvokeScript(script).ConfigureAwait(true);
            return _disposed ? null : result;
        }
        catch (Exception)
        {
            // Script evaluation fails routinely — the page may be mid-navigation,
            // cross-origin, or an error page. Reading a title is never worth
            // surfacing an error for.
            return null;
        }
    }

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        _navigationInProgress = true;
        _latestNavigationRequest = e.Request;
        NavigationStarted?.Invoke(this, new EngineNavigationStartedEventArgs(e.Request));
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        var completesLatestNavigation = RequestsMatch(e.Request, _latestNavigationRequest);

        // Apply again after a new document has arrived. Native zoom owns the
        // whole renderer; only engines without that facility use CSS fallback.
        if (completesLatestNavigation &&
            _windowsZoom.TrySet(_desiredZoomFactor, out var appliedFactor))
        {
            _actualZoomFactor = Math.Clamp(appliedFactor, 0.25d, 5d);
            _ = RestoreDocumentZoomAsync();
        }
        else if (completesLatestNavigation)
        {
            _ = ApplyZoomAsync();
            _actualZoomFactor = _desiredZoomFactor;
        }

        if (completesLatestNavigation)
        {
            _navigationInProgress = false;
            _latestNavigationRequest = null;
        }

        NavigationCompleted?.Invoke(
            this,
            new EngineNavigationCompletedEventArgs(
                e.IsSuccess,
                e.IsSuccess ? null : "Navigation failed.",
                e.Request));
    }

    private static bool RequestsMatch(Uri? completed, Uri? latest) =>
        completed is null ||
        latest is not null &&
        Uri.Compare(
            completed,
            latest,
            UriComponents.HttpRequestUrl,
            UriFormat.SafeUnescaped,
            StringComparison.OrdinalIgnoreCase) == 0;

    private async Task ApplyZoomAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var factor = _desiredZoomFactor.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await _webView.InvokeScript(
                $$"""
                (() => {
                  const root = document.documentElement;
                  if (!root) return;
                  if (!root.hasAttribute('data-aphelion-original-zoom')) {
                    root.setAttribute('data-aphelion-original-zoom', root.style.zoom || '');
                  }
                  root.style.zoom = '{{factor}}';
                })();
                """).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // A document can disappear between a navigation event and script
            // execution. The next successful completion reapplies the scale.
        }
    }

    private void OnAdapterCreated(object? sender, WebViewAdapterEventArgs e) =>
        TryAttachPlatformBridges(e.TryGetPlatformHandle());

    private void OnAdapterDestroyed(object? sender, WebViewAdapterEventArgs e)
    {
        _windowsZoom.Detach();
        _windowsDownloads.Detach();
    }

    private void TryAttachPlatformBridges(Avalonia.Platform.IPlatformHandle? platformHandle)
    {
        if (_disposed)
        {
            return;
        }

        _windowsDownloads.TryAttach(platformHandle);

        if (!_windowsZoom.TryAttach(platformHandle))
        {
            return;
        }

        if (_windowsZoom.TrySet(_desiredZoomFactor, out var appliedFactor))
        {
            _actualZoomFactor = Math.Clamp(appliedFactor, 0.25d, 5d);
            _ = RestoreDocumentZoomAsync();
        }
    }

    private void OnEngineDownloadStarted(IEngineDownloadOperation operation)
    {
        if (!_disposed)
        {
            DownloadStarted?.Invoke(this, new EngineDownloadStartedEventArgs(operation));
        }
    }

    private void OnNativeZoomChanged(double factor)
    {
        if (_disposed || !double.IsFinite(factor))
        {
            return;
        }

        _actualZoomFactor = Math.Clamp(factor, 0.25d, 5d);

        // WebView2 may announce its own remembered site factor while a document
        // is changing. Aphelion's persisted value remains authoritative and is
        // reapplied on completion; only an interaction outside navigation is a
        // user zoom that should update application state.
        if (_navigationInProgress)
        {
            return;
        }

        _desiredZoomFactor = _actualZoomFactor;
        ZoomFactorChanged?.Invoke(this, new EngineZoomFactorChangedEventArgs(_actualZoomFactor));
    }

    private async Task RestoreDocumentZoomAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _webView.InvokeScript(
                """
                (() => {
                  const root = document.documentElement;
                  if (!root || !root.hasAttribute('data-aphelion-original-zoom')) return;
                  const original = root.getAttribute('data-aphelion-original-zoom');
                  if (original) root.style.zoom = original;
                  else root.style.removeProperty('zoom');
                  root.removeAttribute('data-aphelion-original-zoom');
                })();
                """).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // A navigation can replace the document while cleanup is pending.
        }
    }

    /// <summary>
    /// Stops the document and replaces it with an inert page before this native
    /// surface leaves its tab. Removing a hosted native control alone is not a
    /// reliable media-lifecycle signal on every platform, so this explicitly
    /// tears down active audio and video playback first.
    /// </summary>
    public void Close()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _webView.Stop();
            _webView.Navigate(new Uri("about:blank"));
        }
        catch (Exception)
        {
            // The host can already be gone while a window is closing. Either
            // operation is best effort; event handlers are still released below.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _webView.NavigationStarted -= OnNavigationStarted;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _webView.AdapterCreated -= OnAdapterCreated;
        _webView.AdapterDestroyed -= OnAdapterDestroyed;
        _windowsZoom.Dispose();
        _windowsDownloads.Dispose();
        _disposed = true;
    }
}
