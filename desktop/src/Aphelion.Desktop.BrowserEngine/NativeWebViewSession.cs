using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.ValueObjects;
using Avalonia.Controls;
using System.Reflection;

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
    private double _zoomFactor = 1d;

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

    public double SetZoomFactor(double factor)
    {
        if (_disposed)
        {
            return _zoomFactor;
        }

        _zoomFactor = Math.Clamp(factor, 0.25d, 5d);

        if (TrySetNativeZoomFactor(_zoomFactor, out var appliedFactor))
        {
            _zoomFactor = appliedFactor;
            _ = RestoreDocumentZoomAsync();
            return _zoomFactor;
        }

        _ = ApplyZoomAsync();
        return _zoomFactor;
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

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e) =>
        NavigationStarted?.Invoke(this, new EngineNavigationStartedEventArgs(e.Request));

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        // Apply again after a new document has arrived. Native zoom owns the
        // whole renderer; only engines without that facility use CSS fallback.
        if (TrySetNativeZoomFactor(_zoomFactor, out var appliedFactor))
        {
            _zoomFactor = appliedFactor;
            _ = RestoreDocumentZoomAsync();
        }
        else
        {
            _ = ApplyZoomAsync();
        }

        NavigationCompleted?.Invoke(
            this,
            new EngineNavigationCompletedEventArgs(
                e.IsSuccess,
                e.IsSuccess ? null : "Navigation failed.",
                e.Request));
    }

    private async Task ApplyZoomAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var factor = _zoomFactor.ToString(System.Globalization.CultureInfo.InvariantCulture);
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

    /// <summary>
    /// Uses an engine's native zoom controller when the hosted adapter exposes
    /// one. Avalonia's public WebView surface intentionally does not yet expose
    /// portable zoom, but WebView2 does; using its controller avoids CSS zoom's
    /// layout distortion on Windows. Other engines retain the document fallback
    /// until their equivalent controller becomes publicly available.
    /// </summary>
    private bool TrySetNativeZoomFactor(double factor, out double appliedFactor)
    {
        appliedFactor = factor;

        try
        {
            var adapter = typeof(NativeWebView)
                .GetMethod("TryGetAdapter", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(_webView, null);

            if (adapter is null)
            {
                return false;
            }

            for (var type = adapter.GetType(); type is not null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    var controller = field.GetValue(adapter);
                    var setZoom = field.FieldType.GetMethod(
                        "SetZoomFactor",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        binder: null,
                        types: [typeof(double)],
                        modifiers: null);

                    if (setZoom is null)
                    {
                        continue;
                    }

                    setZoom.Invoke(controller, [factor]);
                    DisableBuiltInZoomControls(adapter);

                    var getZoom = field.FieldType.GetMethod(
                        "GetZoomFactor",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        binder: null,
                        types: Type.EmptyTypes,
                        modifiers: null);

                    if (getZoom?.Invoke(controller, null) is double actual)
                    {
                        appliedFactor = Math.Clamp(actual, 0.25d, 5d);
                    }

                    return true;
                }
            }
        }
        catch (Exception)
        {
            // The public adapter has no guaranteed zoom contract. Fallback to
            // the document scale when a platform does not surface a controller.
        }

        return false;
    }

    /// <summary>
    /// Prevents the platform WebView from applying a second, invisible zoom on
    /// Ctrl+wheel. Aphelion owns that gesture so persisted and displayed values
    /// cannot drift away from the renderer.
    /// </summary>
    private static void DisableBuiltInZoomControls(object adapter)
    {
        try
        {
            MethodInfo? tryGetWebView = null;

            for (var type = adapter.GetType(); type is not null && tryGetWebView is null; type = type.BaseType)
            {
                tryGetWebView = type.GetMethod(
                    "TryGetWebView2",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            }

            var webView = tryGetWebView?.Invoke(adapter, null);
            var settings = webView?.GetType().GetMethod("GetSettings")?.Invoke(webView, null);
            settings?.GetType().GetMethod("SetIsZoomControlEnabled")?.Invoke(settings, [false]);
        }
        catch (Exception)
        {
            // Other platform adapters do not expose WebView2 settings.
        }
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
        _disposed = true;
    }
}
