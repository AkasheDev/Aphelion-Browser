using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Aphelion.Desktop.Application.Ports;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Aphelion.Desktop.BrowserEngine.Windows;

/// <summary>
/// Owns the WebView2 COM surface Avalonia's portable WebView contract does not
/// expose: HTML fullscreen inside the page, and accelerator keys (F11, Escape)
/// while the native surface has focus.
/// </summary>
internal sealed class WebView2FullscreenBridge(
    Action<bool> fullScreenElementChanged,
    Action<EngineAcceleratorKeyPressedEventArgs> acceleratorKeyPressed) : IDisposable
{
    private const uint VirtualKeyEscape = 0x1B;
    private const uint VirtualKeyF11 = 0x7A;
    private const int KeyEventKindKeyDown = 0;
    private const int KeyEventKindSystemKeyDown = 2;

    private readonly Action<bool> _fullScreenElementChanged =
        fullScreenElementChanged ?? throw new ArgumentNullException(nameof(fullScreenElementChanged));
    private readonly Action<EngineAcceleratorKeyPressedEventArgs> _acceleratorKeyPressed =
        acceleratorKeyPressed ?? throw new ArgumentNullException(nameof(acceleratorKeyPressed));

    private WebView2Core? _core;
    private WebView2Controller? _controller;
    private WebView2ContainsFullScreenElementChangedCallback? _fullScreenHandler;
    private WebView2AcceleratorKeyPressedCallback? _acceleratorHandler;
    private EventRegistrationToken _fullScreenToken;
    private EventRegistrationToken _acceleratorToken;
    private bool _subscribedCore;
    private bool _subscribedAccelerator;

    public bool TryAttach(IPlatformHandle? platformHandle)
    {
        if (!OperatingSystem.IsWindows() ||
            platformHandle is not IWindowsWebView2PlatformHandle windows)
        {
            return _subscribedCore;
        }

        TryAttachCore(windows);
        TryAttachAccelerator(windows);
        return _subscribedCore;
    }

    private void TryAttachCore(IWindowsWebView2PlatformHandle windows)
    {
        if (_subscribedCore)
        {
            return;
        }

        try
        {
            var corePointer = windows.CoreWebView2;

            if (corePointer == IntPtr.Zero)
            {
                return;
            }

            unsafe
            {
                var pointer = (void*)corePointer;

                try
                {
                    _core = ComInterfaceMarshaller<WebView2Core>.ConvertToManaged(pointer);
                }
                finally
                {
                    ComInterfaceMarshaller<WebView2Core>.Free(pointer);
                }
            }

            if (_core is null)
            {
                return;
            }

            _fullScreenHandler = new WebView2ContainsFullScreenElementChangedCallback(OnContainsFullScreenElementChanged);
            _core.AddContainsFullScreenElementChanged(_fullScreenHandler, out _fullScreenToken);
            _subscribedCore = true;
        }
        catch (Exception)
        {
            DetachCore();
        }
    }

    private void TryAttachAccelerator(IWindowsWebView2PlatformHandle windows)
    {
        if (_subscribedAccelerator)
        {
            return;
        }

        try
        {
            var controllerPointer = windows.CoreWebView2Controller;

            if (controllerPointer == IntPtr.Zero)
            {
                return;
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
                    ComInterfaceMarshaller<WebView2Controller>.Free(pointer);
                }
            }

            if (_controller is null)
            {
                return;
            }

            _acceleratorHandler = new WebView2AcceleratorKeyPressedCallback(OnAcceleratorKeyPressed);
            _controller.AddAcceleratorKeyPressed(_acceleratorHandler, out _acceleratorToken);
            _subscribedAccelerator = true;
        }
        catch (Exception)
        {
            DetachAccelerator();
        }
    }

    private void OnContainsFullScreenElementChanged()
    {
        if (_core is null)
        {
            return;
        }

        try
        {
            _fullScreenElementChanged(_core.GetContainsFullScreenElement() != 0);
        }
        catch (Exception)
        {
            DetachCore();
        }
    }

    private void OnAcceleratorKeyPressed(WebView2AcceleratorKeyPressedArgs args)
    {
        try
        {
            var kind = args.GetKeyEventKind();

            if (kind is not (KeyEventKindKeyDown or KeyEventKindSystemKeyDown))
            {
                return;
            }

            var key = args.GetVirtualKey();

            if (key is not (VirtualKeyF11 or VirtualKeyEscape))
            {
                return;
            }

            if (args.GetPhysicalKeyStatus().WasKeyDown != 0)
            {
                return;
            }

            var e = new EngineAcceleratorKeyPressedEventArgs((int)key, isKeyDown: true);
            _acceleratorKeyPressed(e);

            if (e.Handled)
            {
                args.SetHandled(1);
            }
        }
        catch (Exception)
        {
            DetachAccelerator();
        }
    }

    public void Detach()
    {
        DetachCore();
        DetachAccelerator();
    }

    private void DetachCore()
    {
        if (_subscribedCore && _core is not null)
        {
            try
            {
                _core.RemoveContainsFullScreenElementChanged(_fullScreenToken);
            }
            catch (Exception)
            {
                // Adapter destruction can invalidate the CoreWebView2 first.
            }
        }

        _subscribedCore = false;
        _core = null;
        _fullScreenHandler = null;
        _fullScreenToken = default;
    }

    private void DetachAccelerator()
    {
        if (_subscribedAccelerator && _controller is not null)
        {
            try
            {
                _controller.RemoveAcceleratorKeyPressed(_acceleratorToken);
            }
            catch (Exception)
            {
                // Adapter destruction can invalidate the controller first.
            }
        }

        _subscribedAccelerator = false;
        _controller = null;
        _acceleratorHandler = null;
        _acceleratorToken = default;
    }

    public void Dispose() => Detach();
}

/// <summary>
/// ICoreWebView2, declared flat through get_ContainsFullScreenElement. COM
/// interface inheritance is one vtable, so the 49 members that precede the
/// fullscreen trio — starting get_Settings, get_Source, Navigate,
/// NavigateToString, then the navigation events through OpenDevToolsWindow —
/// are padding. Padding methods are never invoked; only their count matters.
/// Count matches the download bridge's 58-slot ICoreWebView2 block
/// (SDK 1.0.3719.40): this interface names slots 49–51 of those 58.
/// </summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("76ECEACB-0462-4D94-AC83-423A6793775E")]
internal partial interface WebView2Core
{
    void Reserved00(); void Reserved01(); void Reserved02(); void Reserved03();
    void Reserved04(); void Reserved05(); void Reserved06(); void Reserved07();
    void Reserved08(); void Reserved09(); void Reserved10(); void Reserved11();
    void Reserved12(); void Reserved13(); void Reserved14(); void Reserved15();
    void Reserved16(); void Reserved17(); void Reserved18(); void Reserved19();
    void Reserved20(); void Reserved21(); void Reserved22(); void Reserved23();
    void Reserved24(); void Reserved25(); void Reserved26(); void Reserved27();
    void Reserved28(); void Reserved29(); void Reserved30(); void Reserved31();
    void Reserved32(); void Reserved33(); void Reserved34(); void Reserved35();
    void Reserved36(); void Reserved37(); void Reserved38(); void Reserved39();
    void Reserved40(); void Reserved41(); void Reserved42(); void Reserved43();
    void Reserved44(); void Reserved45(); void Reserved46(); void Reserved47();
    void Reserved48();

    void AddContainsFullScreenElementChanged(
        [MarshalAs(UnmanagedType.Interface)] WebView2ContainsFullScreenElementChangedHandler eventHandler,
        out EventRegistrationToken token);

    void RemoveContainsFullScreenElementChanged(EventRegistrationToken token);

    int GetContainsFullScreenElement();
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("e45d98b1-afef-45be-8baf-6c7728867f73")]
internal partial interface WebView2ContainsFullScreenElementChangedHandler
{
    void Invoke(IntPtr sender, IntPtr args);
}

[GeneratedComClass]
internal sealed partial class WebView2ContainsFullScreenElementChangedCallback(Action callback)
    : WebView2ContainsFullScreenElementChangedHandler
{
    private readonly Action _callback = callback;

    public void Invoke(IntPtr sender, IntPtr args) => _callback();
}

[StructLayout(LayoutKind.Sequential)]
internal struct WebView2PhysicalKeyStatus
{
    public uint RepeatCount;
    public uint ScanCode;
    public int IsExtendedKey;
    public int IsMenuKeyDown;
    public int WasKeyDown;
    public int IsKeyReleased;
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("9f760f8a-fb79-42be-9990-7b56900fa9c7")]
internal partial interface WebView2AcceleratorKeyPressedArgs
{
    int GetKeyEventKind();

    uint GetVirtualKey();

    int GetKeyEventLParam();

    WebView2PhysicalKeyStatus GetPhysicalKeyStatus();

    int GetHandled();

    void SetHandled(int value);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("b29c7e28-fa79-41a8-8e44-65811c76dcb2")]
internal partial interface WebView2AcceleratorKeyPressedHandler
{
    void Invoke(
        IntPtr sender,
        [MarshalAs(UnmanagedType.Interface)] WebView2AcceleratorKeyPressedArgs args);
}

[GeneratedComClass]
internal sealed partial class WebView2AcceleratorKeyPressedCallback(
    Action<WebView2AcceleratorKeyPressedArgs> callback)
    : WebView2AcceleratorKeyPressedHandler
{
    private readonly Action<WebView2AcceleratorKeyPressedArgs> _callback = callback;

    public void Invoke(IntPtr sender, WebView2AcceleratorKeyPressedArgs args) => _callback(args);
}
