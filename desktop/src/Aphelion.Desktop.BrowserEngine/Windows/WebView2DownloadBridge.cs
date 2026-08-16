using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Aphelion.Desktop.Application.Ports;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Aphelion.Desktop.BrowserEngine.Windows;

/// <summary>
/// Owns the piece of WebView2 COM surface that Avalonia's portable WebView
/// contract does not expose: the DownloadStarting event and the download
/// operation behind it.
/// </summary>
/// <remarks>
/// The CoreWebView2 pointer comes from Avalonia's public
/// <see cref="IWindowsWebView2PlatformHandle"/>, exactly as the zoom bridge
/// takes the controller pointer. Every DownloadStarting is marked handled, so
/// WebView2's own download bar never appears — Aphelion's downloads UI is the
/// only one the user sees. All platform-specific details stay inside the
/// browser-engine adapter; the application only observes a portable
/// <see cref="IEngineDownloadOperation"/>.
/// </remarks>
internal sealed class WebView2DownloadBridge(Action<IEngineDownloadOperation> downloadStarted) : IDisposable
{
    private readonly Action<IEngineDownloadOperation> _downloadStarted =
        downloadStarted ?? throw new ArgumentNullException(nameof(downloadStarted));

    private WebView2Core4? _core;
    private WebView2DownloadStartingCallback? _handler;
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
            var corePointer = windows.CoreWebView2;

            if (corePointer == IntPtr.Zero)
            {
                return false;
            }

            unsafe
            {
                var pointer = (void*)corePointer;

                try
                {
                    // The cast inside the marshaller queries for ICoreWebView2_4,
                    // the first version with DownloadStarting. A runtime too old
                    // to answer it lands in the catch and leaves downloads to
                    // the platform's default handling.
                    _core = ComInterfaceMarshaller<WebView2Core4>.ConvertToManaged(pointer);
                }
                finally
                {
                    // The public platform handle returns an owned COM reference.
                    // The managed wrapper keeps its own reference after conversion.
                    ComInterfaceMarshaller<WebView2Core4>.Free(pointer);
                }
            }

            if (_core is null)
            {
                return false;
            }

            _handler = new WebView2DownloadStartingCallback(OnDownloadStarting);
            _core.AddDownloadStarting(_handler, out _token);
            _subscribed = true;
            return true;
        }
        catch (Exception)
        {
            Detach();
            return false;
        }
    }

    private void OnDownloadStarting(WebView2DownloadStartingArgs args)
    {
        try
        {
            var operation = new WebView2DownloadOperation(args.GetDownloadOperation());

            // Handled hides WebView2's own download dialog; the transfer itself
            // proceeds untouched, on the default uniquified path in the user's
            // downloads folder.
            args.SetHandled(1);

            _downloadStarted(operation);
        }
        catch (Exception)
        {
            // A download the bridge cannot read still downloads through the
            // engine's default pipeline; it must never take the page with it.
        }
    }

    public void Detach()
    {
        if (_subscribed && _core is not null)
        {
            try
            {
                _core.RemoveDownloadStarting(_token);
            }
            catch (Exception)
            {
                // Adapter destruction can invalidate the CoreWebView2 first.
            }
        }

        _subscribed = false;
        _core = null;
        _handler = null;
        _token = default;
    }

    public void Dispose() => Detach();
}

/// <summary>
/// Wraps one live ICoreWebView2DownloadOperation as the portable handle the
/// application layer sees.
/// </summary>
/// <remarks>
/// Values are cached as the engine reports them, so the handle can still
/// answer questions after the underlying COM object died with its web view.
/// Event subscriptions are released as soon as the download reaches a terminal
/// state; nothing further can happen to it.
/// </remarks>
internal sealed class WebView2DownloadOperation : IEngineDownloadOperation
{
    private readonly WebView2Download _download;
    private WebView2BytesReceivedChangedCallback? _bytesHandler;
    private WebView2DownloadStateChangedCallback? _stateHandler;
    private EventRegistrationToken _bytesToken;
    private EventRegistrationToken _stateToken;
    private bool _subscribed;

    private long _bytesReceived;
    private long? _totalBytes;

    private const int StateInterrupted = 1;
    private const int StateCompleted = 2;
    private const int ReasonUserCanceled = 26;
    private const int ReasonUserShutdown = 27;
    private const int ReasonUserPaused = 28;

    public WebView2DownloadOperation(WebView2Download download)
    {
        _download = download ?? throw new ArgumentNullException(nameof(download));

        Source = Uri.TryCreate(TryRead(_download.GetUri), UriKind.Absolute, out var source)
            ? source
            : null;
        FilePath = TryRead(_download.GetResultFilePath);
        _totalBytes = NormalizeTotal(TryRead(_download.GetTotalBytesToReceive, -1L));
        _bytesReceived = TryRead(_download.GetBytesReceived, 0L);

        try
        {
            _bytesHandler = new WebView2BytesReceivedChangedCallback(OnBytesReceivedChanged);
            _stateHandler = new WebView2DownloadStateChangedCallback(OnStateChanged);
            _download.AddBytesReceivedChanged(_bytesHandler, out _bytesToken);
            _download.AddStateChanged(_stateHandler, out _stateToken);
            _subscribed = true;
        }
        catch (Exception)
        {
            Unsubscribe();
        }
    }

    public Uri? Source { get; }

    public string? FilePath { get; }

    public long BytesReceived => _bytesReceived;

    public long? TotalBytes => _totalBytes;

    public bool CanResume => TryRead(_download.GetCanResume, 0) != 0;

    public event EventHandler? ProgressChanged;

    public event EventHandler<EngineDownloadStateChangedEventArgs>? StateChanged;

    public bool Pause() => TryInvoke(_download.Pause);

    public bool ResumeDownload() => TryInvoke(_download.Resume);

    public bool Cancel() => TryInvoke(_download.Cancel);

    private void OnBytesReceivedChanged()
    {
        _bytesReceived = TryRead(_download.GetBytesReceived, _bytesReceived);
        _totalBytes = NormalizeTotal(TryRead(_download.GetTotalBytesToReceive, -1L)) ?? _totalBytes;
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnStateChanged()
    {
        var state = TryRead(_download.GetState, StateInterrupted);

        if (state == StateCompleted)
        {
            _bytesReceived = TryRead(_download.GetBytesReceived, _bytesReceived);
            Unsubscribe();
            StateChanged?.Invoke(
                this,
                new EngineDownloadStateChangedEventArgs(EngineDownloadState.Completed));
            return;
        }

        if (state != StateInterrupted)
        {
            // Back in progress — a paused or interrupted transfer resumed.
            StateChanged?.Invoke(
                this,
                new EngineDownloadStateChangedEventArgs(EngineDownloadState.InProgress));
            return;
        }

        var reason = TryRead(_download.GetInterruptReason, 0);

        switch (reason)
        {
            case ReasonUserPaused:
                StateChanged?.Invoke(
                    this,
                    new EngineDownloadStateChangedEventArgs(EngineDownloadState.Paused));
                return;

            case ReasonUserCanceled:
            case ReasonUserShutdown:
                Unsubscribe();
                StateChanged?.Invoke(
                    this,
                    new EngineDownloadStateChangedEventArgs(EngineDownloadState.Canceled));
                return;

            default:
                Unsubscribe();
                StateChanged?.Invoke(
                    this,
                    new EngineDownloadStateChangedEventArgs(
                        EngineDownloadState.Failed,
                        DescribeInterruptReason(reason)));
                return;
        }
    }

    /// <summary>
    /// Folds WebView2's thirty interrupt reasons into the handful of phrases a
    /// person can act on. The numeric bands follow the IDL's grouping: file
    /// reasons 1–11, network 12–16, server 17–25.
    /// </summary>
    private static string DescribeInterruptReason(int reason) => reason switch
    {
        3 => "Not enough disk space",
        6 or 8 or 9 => "Blocked for security reasons",
        >= 1 and <= 11 => "Could not write the file",
        >= 12 and <= 16 => "Network error",
        >= 17 and <= 25 => "Server error",
        29 => "The download process crashed",
        _ => "Download failed",
    };

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            _bytesHandler = null;
            _stateHandler = null;
            return;
        }

        try
        {
            _download.RemoveBytesReceivedChanged(_bytesToken);
            _download.RemoveStateChanged(_stateToken);
        }
        catch (Exception)
        {
            // The web view owning the download may already be gone.
        }

        _subscribed = false;
        _bytesHandler = null;
        _stateHandler = null;
        _bytesToken = default;
        _stateToken = default;
    }

    private static long? NormalizeTotal(long total) => total > 0 ? total : null;

    private static string? TryRead(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static T TryRead<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static bool TryInvoke(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// ICoreWebView2_4, declared flat. COM interface inheritance is one vtable, so
/// the 70 members inherited from ICoreWebView2, _2 and _3 — and the two
/// FrameCreated members this application does not use — are declared as padding
/// to keep the download members on their correct slots. Padding methods are
/// never invoked, so their signatures are irrelevant; only their count matters.
/// Order and count verified against WebView2.idl (SDK 1.0.3719.40).
/// </summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("20d02d59-6df2-42dc-bd06-f98a694b1302")]
internal partial interface WebView2Core4
{
    // ICoreWebView2 — 58 slots.
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
    void Reserved48(); void Reserved49(); void Reserved50(); void Reserved51();
    void Reserved52(); void Reserved53(); void Reserved54(); void Reserved55();
    void Reserved56(); void Reserved57();

    // ICoreWebView2_2 — 7 slots.
    void Reserved58(); void Reserved59(); void Reserved60(); void Reserved61();
    void Reserved62(); void Reserved63(); void Reserved64();

    // ICoreWebView2_3 — 5 slots.
    void Reserved65(); void Reserved66(); void Reserved67(); void Reserved68();
    void Reserved69();

    // ICoreWebView2_4: add_FrameCreated, remove_FrameCreated (unused), then
    // the two members this bridge exists for.
    void Reserved70(); void Reserved71();

    void AddDownloadStarting(
        [MarshalAs(UnmanagedType.Interface)] WebView2DownloadStartingHandler eventHandler,
        out EventRegistrationToken token);

    void RemoveDownloadStarting(EventRegistrationToken token);
}

/// <summary>ICoreWebView2DownloadStartingEventArgs. GetDeferral, the trailing
/// slot, is omitted because it is never called.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("e99bbe21-43e9-4544-a732-282764eafa60")]
internal partial interface WebView2DownloadStartingArgs
{
    [return: MarshalAs(UnmanagedType.Interface)]
    WebView2Download GetDownloadOperation();

    int GetCancel();

    void SetCancel(int value);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetResultFilePath();

    void SetResultFilePath([MarshalAs(UnmanagedType.LPWStr)] string value);

    int GetHandled();

    void SetHandled(int value);
}

/// <summary>ICoreWebView2DownloadOperation. The EstimatedEndTimeChanged pair is
/// padding: Aphelion computes its own rates from BytesReceivedChanged.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("3d6b6cf2-afe1-44c7-a995-c65117714336")]
internal partial interface WebView2Download
{
    void AddBytesReceivedChanged(
        [MarshalAs(UnmanagedType.Interface)] WebView2BytesReceivedChangedHandler eventHandler,
        out EventRegistrationToken token);

    void RemoveBytesReceivedChanged(EventRegistrationToken token);

    void ReservedAddEstimatedEndTimeChanged();

    void ReservedRemoveEstimatedEndTimeChanged();

    void AddStateChanged(
        [MarshalAs(UnmanagedType.Interface)] WebView2DownloadStateChangedHandler eventHandler,
        out EventRegistrationToken token);

    void RemoveStateChanged(EventRegistrationToken token);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetUri();

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetContentDisposition();

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetMimeType();

    long GetTotalBytesToReceive();

    long GetBytesReceived();

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetEstimatedEndTime();

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetResultFilePath();

    int GetState();

    int GetInterruptReason();

    void Cancel();

    void Pause();

    void Resume();

    int GetCanResume();
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("efedc989-c396-41ca-83f7-07f845a55724")]
internal partial interface WebView2DownloadStartingHandler
{
    void Invoke(
        IntPtr sender,
        [MarshalAs(UnmanagedType.Interface)] WebView2DownloadStartingArgs args);
}

[GeneratedComClass]
internal sealed partial class WebView2DownloadStartingCallback(Action<WebView2DownloadStartingArgs> callback)
    : WebView2DownloadStartingHandler
{
    private readonly Action<WebView2DownloadStartingArgs> _callback = callback;

    public void Invoke(IntPtr sender, WebView2DownloadStartingArgs args) => _callback(args);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("828e8ab6-d94c-4264-9cef-5217170d6251")]
internal partial interface WebView2BytesReceivedChangedHandler
{
    void Invoke(IntPtr sender, IntPtr args);
}

[GeneratedComClass]
internal sealed partial class WebView2BytesReceivedChangedCallback(Action callback)
    : WebView2BytesReceivedChangedHandler
{
    private readonly Action _callback = callback;

    public void Invoke(IntPtr sender, IntPtr args) => _callback();
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("81336594-7ede-4ba9-bf71-acf0a95b58dd")]
internal partial interface WebView2DownloadStateChangedHandler
{
    void Invoke(IntPtr sender, IntPtr args);
}

[GeneratedComClass]
internal sealed partial class WebView2DownloadStateChangedCallback(Action callback)
    : WebView2DownloadStateChangedHandler
{
    private readonly Action _callback = callback;

    public void Invoke(IntPtr sender, IntPtr args) => _callback();
}
