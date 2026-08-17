using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.BrowserEngine.Windows;
using Aphelion.Desktop.Domain.ValueObjects;
using Avalonia.Controls;
using Avalonia.Threading;
using System.Text.Json;

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
    private readonly WebView2FullscreenBridge _windowsFullscreen;
    private readonly DispatcherTimer _pagePoll;
    private bool _disposed;
    private bool _navigationInProgress;
    private bool _muted;
    private Uri? _latestNavigationRequest;
    private double _desiredZoomFactor = 1d;
    private double _actualZoomFactor = 1d;

    public NativeWebViewSession(NativeWebView webView, Func<string?>? downloadDirectory = null)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _windowsZoom = new WebView2ZoomBridge(OnNativeZoomChanged);
        _windowsDownloads = new WebView2DownloadBridge(OnEngineDownloadStarted, downloadDirectory);
        _windowsFullscreen = new WebView2FullscreenBridge(
            OnEngineFullScreenElementChanged,
            OnEngineAcceleratorKeyPressed);
        _pagePoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _pagePoll.Tick += OnPagePollTick;

        _webView.NavigationStarted += OnNavigationStarted;
        _webView.NavigationCompleted += OnNavigationCompleted;
        _webView.AdapterCreated += OnAdapterCreated;
        _webView.AdapterDestroyed += OnAdapterDestroyed;

        TryAttachPlatformBridges(_webView.TryGetPlatformHandle());
        _pagePoll.Start();
    }

    public bool CanGoBack => _webView.CanGoBack;

    public bool CanGoForward => _webView.CanGoForward;

    public event EventHandler<EngineNavigationStartedEventArgs>? NavigationStarted;

    public event EventHandler<EngineNavigationCompletedEventArgs>? NavigationCompleted;

    public event EventHandler<EngineZoomFactorChangedEventArgs>? ZoomFactorChanged;

    public event EventHandler<EngineDownloadStartedEventArgs>? DownloadStarted;

    public event EventHandler<EngineFullScreenElementChangedEventArgs>? FullScreenElementChanged;

    public event EventHandler<EngineAcceleratorKeyPressedEventArgs>? AcceleratorKeyPressed;

    public event EventHandler<EnginePageMessageEventArgs>? PageMessage;

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

    public async Task<EngineFindResult?> FindAsync(string text, bool forward, bool matchCase)
    {
        if (_disposed)
        {
            return null;
        }

        var query = EscapeJavaScript(text ?? string.Empty);
        var result = await EvaluateAsync(
            $$"""
            (() => {
              const q = '{{query}}';
              const cs = {{(matchCase ? "true" : "false")}};
              const back = {{(forward ? "false" : "true")}};
              if (!q) {
                window.getSelection()?.removeAllRanges();
                return '0|0';
              }
              const hay = document.body ? document.body.innerText : '';
              const src = cs ? hay : hay.toLowerCase();
              const needle = cs ? q : q.toLowerCase();
              let count = 0, from = 0;
              while (needle && (from = src.indexOf(needle, from)) >= 0) {
                count++;
                from += needle.length;
              }
              const found = window.find(q, cs, back, true, false, false, false);
              return count + '|' + (found ? '1' : '0');
            })()
            """).ConfigureAwait(true);

        if (result is null)
        {
            return null;
        }

        var parts = result.Trim().Trim('"').Split('|');
        var count = parts.Length > 0 && int.TryParse(parts[0], out var n) ? n : 0;
        var active = parts.Length > 1 && parts[1] == "1" && count > 0 ? 1 : 0;
        return new EngineFindResult(count, active);
    }

    public async Task StopFindAsync() =>
        await EvaluateAsync("window.getSelection()?.removeAllRanges(); 'ok';").ConfigureAwait(true);

    public async Task PrintAsync()
    {
        if (_disposed)
        {
            return;
        }

        var showPrint = _webView.GetType().GetMethod("ShowPrintUI", Type.EmptyTypes);
        if (showPrint is not null)
        {
            try
            {
                showPrint.Invoke(_webView, null);
                return;
            }
            catch (Exception)
            {
                // Fall through to the document's own print.
            }
        }

        await EvaluateAsync("window.print(); 'ok';").ConfigureAwait(true);
    }

    public bool OpenDevTools() => !_disposed && _windowsFullscreen.TryOpenDevTools();

    public async Task SetMutedAsync(bool muted)
    {
        _muted = muted;
        var flag = muted ? "true" : "false";
        await EvaluateAsync(
            $$"""
            (() => {
              document.querySelectorAll('audio,video').forEach(el => { el.muted = {{flag}}; });
              return 'ok';
            })()
            """).ConfigureAwait(true);
    }

    public IEngineDownloadOperation StartDownload(Uri source, string filePath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var operation = new HttpDownloadOperation(source, filePath);
        operation.Start();
        return operation;
    }

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (e.Request is { } request &&
            (request.Scheme == Uri.UriSchemeHttp || request.Scheme == Uri.UriSchemeHttps))
        {
            _navigationInProgress = true;
            _latestNavigationRequest = request;
        }

        NavigationStarted?.Invoke(this, new EngineNavigationStartedEventArgs(e.Request));
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        var completesLatestNavigation = CompletesLatestNavigation(e.Request, _latestNavigationRequest, e.IsSuccess);

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
            _ = InstallPageHooksAsync();
            if (_muted)
            {
                _ = SetMutedAsync(true);
            }
        }

        if (!completesLatestNavigation)
        {
            return;
        }

        _navigationInProgress = false;
        _latestNavigationRequest = null;

        NavigationCompleted?.Invoke(
            this,
            new EngineNavigationCompletedEventArgs(
                e.IsSuccess,
                e.IsSuccess ? null : "Navigation failed.",
                e.Request));
    }

    /// <summary>
    /// A failed completion with no URL is a blank surface or subframe. Loopback
    /// aliases (localhost / 127.0.0.1) count as the same navigation, as Chrome
    /// treats them; other URL changes are redirects and update
    /// <c>_latestNavigationRequest</c> via NavigationStarted first.
    /// </summary>
    private static bool CompletesLatestNavigation(Uri? completed, Uri? latest, bool success)
    {
        if (latest is null)
        {
            return false;
        }

        if (completed is null)
        {
            return success;
        }

        if (Uri.Compare(
                completed,
                latest,
                UriComponents.HttpRequestUrl,
                UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase) == 0)
        {
            return true;
        }

        return AreLoopbackAliases(completed, latest);
    }

    private static bool AreLoopbackAliases(Uri left, Uri right)
    {
        if (left.Port != right.Port)
        {
            return false;
        }

        return IsLoopbackHost(left.Host) && IsLoopbackHost(right.Host);
    }

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "127.0.0.1", StringComparison.Ordinal) ||
        string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);

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
        _windowsFullscreen.Detach();
    }

    private void TryAttachPlatformBridges(Avalonia.Platform.IPlatformHandle? platformHandle)
    {
        if (_disposed)
        {
            return;
        }

        _windowsDownloads.TryAttach(platformHandle);
        _windowsFullscreen.TryAttach(platformHandle);

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

    private void OnEngineFullScreenElementChanged(bool containsFullScreenElement)
    {
        if (!_disposed)
        {
            FullScreenElementChanged?.Invoke(
                this,
                new EngineFullScreenElementChangedEventArgs(containsFullScreenElement));
        }
    }

    private void OnEngineAcceleratorKeyPressed(EngineAcceleratorKeyPressedEventArgs e)
    {
        if (!_disposed)
        {
            AcceleratorKeyPressed?.Invoke(this, e);
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

        _pagePoll.Stop();
        _pagePoll.Tick -= OnPagePollTick;
        _webView.NavigationStarted -= OnNavigationStarted;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _webView.AdapterCreated -= OnAdapterCreated;
        _webView.AdapterDestroyed -= OnAdapterDestroyed;
        _windowsZoom.Dispose();
        _windowsDownloads.Dispose();
        _windowsFullscreen.Dispose();
        _disposed = true;
    }

    private async Task InstallPageHooksAsync()
    {
        await EvaluateAsync(
            """
            (() => {
              if (window.__aphelionInstalled) return 'ok';
              window.__aphelionInstalled = true;
              window.__aphelionQ = [];
              document.addEventListener('contextmenu', function(e) {
                e.preventDefault();
                e.stopPropagation();
                var a = e.target.closest && e.target.closest('a');
                var img = e.target.closest && e.target.closest('img');
                window.__aphelionQ.push({
                  t: 'ctx',
                  x: e.clientX,
                  y: e.clientY,
                  href: (a && a.href) || '',
                  src: (img && img.src) || '',
                  sel: String((window.getSelection && window.getSelection()) || '')
                });
              }, true);
              document.addEventListener('click', function(e) {
                var a = e.target.closest && e.target.closest('a[download]');
                if (!a || !a.href) return;
                e.preventDefault();
                window.__aphelionQ.push({
                  t: 'download',
                  href: a.href,
                  name: a.getAttribute('download') || ''
                });
              }, true);
              document.addEventListener('fullscreenchange', function() {
                window.__aphelionQ.push({ t: 'fs', on: !!document.fullscreenElement });
              });
              return 'ok';
            })()
            """).ConfigureAwait(true);
    }

    private async void OnPagePollTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var raw = await EvaluateAsync(
            "(() => { const q = window.__aphelionQ || []; window.__aphelionQ = []; return JSON.stringify(q); })()")
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(raw) || raw is "[]" or "\"[]\"")
        {
            return;
        }

        try
        {
            var json = raw.Trim().Trim('"').Replace("\\\"", "\"", StringComparison.Ordinal);
            using var doc = JsonDocument.Parse(json.StartsWith('[') ? json : raw.Trim().Trim('"'));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var kind = item.TryGetProperty("t", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                PageMessage?.Invoke(this, new EnginePageMessageEventArgs(kind, item.GetRawText()));
            }
        }
        catch (Exception)
        {
            // A malformed page payload is ignored; polling continues.
        }
    }

    private static string EscapeJavaScript(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
