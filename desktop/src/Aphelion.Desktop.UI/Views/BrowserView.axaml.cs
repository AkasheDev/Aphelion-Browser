using Aphelion.Desktop.BrowserEngine;
using Aphelion.Desktop.UI.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Aphelion.Desktop.UI.Views;

public partial class BrowserView : UserControl, IDisposable
{
    private NativeWebViewSession? _session;
    private BrowserViewModel? _attachedViewModel;

    public BrowserView()
    {
        InitializeComponent();
        this.FindControl<NativeWebView>("WebView")!.EnvironmentRequested += OnEnvironmentRequested;
    }

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
        // Hidden tabs stay in the visual tree with IsVisible=false. Disposing
        // here used to blank the engine and force a reload on the next show.
        base.OnUnloaded(e);
    }

    public void Dispose()
    {
        ReleaseSession();
        GC.SuppressFinalize(this);
    }

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

        var session = new NativeWebViewSession(webView, () => viewModel.PreferredDownloadDirectory);
        _session = session;
        _attachedViewModel = viewModel;

        viewModel.AttachSession(session);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BrowserViewModel.IsPageMenuOpen) ||
            DataContext is not BrowserViewModel { IsPageMenuOpen: true } ||
            this.FindControl<ContextMenu>("PageMenu") is not { } menu)
        {
            return;
        }

        menu.Open(this.FindControl<NativeWebView>("WebView") as Control ?? this);
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

        viewModel?.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel?.DetachSession(session);
        session.Close();
        session.Dispose();
    }

    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        if (DataContext is not BrowserViewModel viewModel || !viewModel.IsPrivate)
        {
            return;
        }

        var directory = viewModel.IsolatedUserDataDirectory;
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        switch (e)
        {
            case WindowsWebView2EnvironmentRequestedEventArgs windows:
                windows.IsInPrivateModeEnabled = true;
                TrySetProperty(windows, "UserDataFolder", directory);
                break;
            case AppleWKWebViewEnvironmentRequestedEventArgs apple:
                apple.NonPersistentDataStore = true;
                TrySetProperty(apple, "UserDataFolder", directory);
                break;
            default:
                TrySetProperty(e, "IsInPrivateModeEnabled", true);
                TrySetProperty(e, "NonPersistentDataStore", true);
                TrySetProperty(e, "UserDataFolder", directory);
                break;
        }
    }

    private static void TrySetProperty(object target, string name, object? value)
    {
        if (value is null)
        {
            return;
        }

        var property = target.GetType().GetProperty(name);
        if (property is { CanWrite: true } && property.PropertyType.IsInstanceOfType(value))
        {
            property.SetValue(target, value);
        }
    }
}
