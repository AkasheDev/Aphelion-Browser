using Aphelion.Desktop.BrowserEngine;
using Aphelion.Desktop.UI.ViewModels;
using Avalonia.Controls;

namespace Aphelion.Desktop.UI.Views;

public partial class BrowserView : UserControl, IDisposable
{
    private NativeWebViewSession? _session;

    public BrowserView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AttachSession();
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        Dispose();
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Wraps the native web view in an engine session and hands it to the view
    /// model, which never sees the web view type itself.
    /// </summary>
    private void AttachSession()
    {
        if (DataContext is not BrowserViewModel viewModel)
        {
            return;
        }

        var webView = this.FindControl<NativeWebView>("WebView");

        if (webView is null)
        {
            return;
        }

        _session?.Dispose();
        _session = new NativeWebViewSession(webView);

        viewModel.AttachSession(_session);
    }
}
