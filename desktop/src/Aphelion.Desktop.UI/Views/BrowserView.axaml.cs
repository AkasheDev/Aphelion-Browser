using Aphelion.Desktop.BrowserEngine;
using Aphelion.Desktop.UI.ViewModels;
using Avalonia.Controls;

namespace Aphelion.Desktop.UI.Views;

public partial class BrowserView : UserControl, IDisposable
{
    private NativeWebViewSession? _session;
    private BrowserViewModel? _attachedViewModel;

    public BrowserView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        ReleaseSession();
        base.OnDataContextChanged(e);
        AttachSession();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        AttachSession();
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        Dispose();
    }

    public void Dispose()
    {
        ReleaseSession();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Wraps the native web view in an engine session and hands it to the view
    /// model, which never sees the web view type itself.
    /// </summary>
    private void AttachSession()
    {
        if (DataContext is not BrowserViewModel viewModel ||
            _session is not null && ReferenceEquals(_attachedViewModel, viewModel))
        {
            return;
        }

        var webView = this.FindControl<NativeWebView>("WebView");

        if (webView is null)
        {
            return;
        }

        ReleaseSession();

        var session = new NativeWebViewSession(webView);
        _session = session;
        _attachedViewModel = viewModel;

        viewModel.AttachSession(session);
    }

    private void ReleaseSession()
    {
        var session = _session;
        var viewModel = _attachedViewModel;

        _session = null;
        _attachedViewModel = null;

        if (session is null)
        {
            return;
        }

        viewModel?.DetachSession(session);
        // A native web view can continue media playback after it leaves the
        // Avalonia visual tree. Explicitly blank it before releasing handlers.
        session.Close();
        session.Dispose();
    }
}
