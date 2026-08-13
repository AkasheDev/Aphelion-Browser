using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Aphelion.Desktop.BrowserEngine.Windows;

/// <summary>
/// Owns the small piece of WebView2 COM surface that Avalonia's portable WebView
/// contract does not expose: the actual zoom factor and its change event.
/// </summary>
/// <remarks>
/// The controller pointer comes from Avalonia's public
/// <see cref="IWindowsWebView2PlatformHandle"/>. All platform-specific details
/// stay inside the browser-engine adapter; the application only observes a
/// portable factor through <c>IBrowserEngineSession</c>.
/// </remarks>
internal sealed class WebView2ZoomBridge(Action<double> zoomChanged) : IDisposable
{
    private readonly Action<double> _zoomChanged = zoomChanged ?? throw new ArgumentNullException(nameof(zoomChanged));
    private WebView2Controller? _controller;
    private WebView2ZoomChangedHandler? _handler;
    private EventRegistrationToken _token;
    private bool _subscribed;

    public bool TryAttach(IPlatformHandle? platformHandle)
    {
        if (_subscribed ||
            !OperatingSystem.IsWindows() ||
            platformHandle is not IWindowsWebView2PlatformHandle windows)
        {
            return _subscribed;
        }

        try
        {
            var controllerPointer = windows.CoreWebView2Controller;

            if (controllerPointer == IntPtr.Zero)
            {
                return false;
            }

            unsafe
            {
                var pointer = (void*)controllerPointer;

                try
                {
                    _controller = ComInterfaceMarshaller<WebView2Controller>.ConvertToManaged(pointer);
                }
                finally
                {
                    // The public platform handle returns an owned COM reference.
                    // The managed wrapper keeps its own reference after conversion.
                    ComInterfaceMarshaller<WebView2Controller>.Free(pointer);
                }
            }

            if (_controller is null)
            {
                return false;
            }

            _handler = new WebView2ZoomChangedHandler(OnZoomChanged);
            _controller.AddZoomFactorChanged(_handler, out _token);
            _subscribed = true;
            return true;
        }
        catch (Exception)
        {
            Detach();
            return false;
        }
    }

    public bool TrySet(double factor, out double appliedFactor)
    {
        appliedFactor = factor;

        if (!_subscribed || _controller is null)
        {
            return false;
        }

        try
        {
            _controller.SetZoomFactor(factor);
            appliedFactor = _controller.GetZoomFactor();
            return true;
        }
        catch (Exception)
        {
            Detach();
            return false;
        }
    }

    private void OnZoomChanged(WebView2Controller controller)
    {
        try
        {
            _zoomChanged(controller.GetZoomFactor());
        }
        catch (Exception)
        {
            Detach();
        }
    }

    public void Detach()
    {
        if (_subscribed && _controller is not null)
        {
            try
            {
                _controller.RemoveZoomFactorChanged(_token);
            }
            catch (Exception)
            {
                // Adapter destruction can invalidate the controller first.
            }
        }

        _subscribed = false;
        _controller = null;
        _handler = null;
        _token = default;
    }

    public void Dispose() => Detach();
}

[StructLayout(LayoutKind.Sequential)]
internal struct WebView2Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct EventRegistrationToken
{
    public long Value;
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("4D00C0D1-9434-4EB6-8078-8697A560334F")]
internal partial interface WebView2Controller
{
    int GetIsVisible();

    void SetIsVisible(int value);

    WebView2Rect GetBounds();

    void SetBounds(WebView2Rect value);

    double GetZoomFactor();

    void SetZoomFactor(double value);

    void AddZoomFactorChanged(
        [MarshalAs(UnmanagedType.Interface)] WebView2ZoomFactorChangedHandler eventHandler,
        out EventRegistrationToken token);

    void RemoveZoomFactorChanged(EventRegistrationToken token);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("B52D71D6-C4DF-4543-A90C-64A3E60F38CB")]
internal partial interface WebView2ZoomFactorChangedHandler
{
    void Invoke(
        [MarshalAs(UnmanagedType.Interface)] WebView2Controller sender,
        IntPtr args);
}

[GeneratedComClass]
internal sealed partial class WebView2ZoomChangedHandler(Action<WebView2Controller> callback)
    : WebView2ZoomFactorChangedHandler
{
    private readonly Action<WebView2Controller> _callback = callback;

    public void Invoke(WebView2Controller sender, IntPtr args) => _callback(sender);
}
