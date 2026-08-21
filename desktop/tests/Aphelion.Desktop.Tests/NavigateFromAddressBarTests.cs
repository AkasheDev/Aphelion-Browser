using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Application.UseCases;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using Xunit;

namespace Aphelion.Desktop.Tests;

public sealed class NavigateFromAddressBarTests
{
    [Fact]
    public void Navigates_when_the_input_is_a_host()
    {
        var session = new FakeEngine();
        var tab = new BrowserTab(TabId.New());
        var useCase = new NavigateFromAddressBar(new FakeSearch("https://duckduckgo.com/?q="));

        Assert.True(useCase.Execute(tab, session, "example.com"));
        Assert.Equal("example.com", tab.Address?.DisplayHost);
        Assert.NotNull(session.LastNavigated);
    }

    [Fact]
    public void Searches_when_the_input_is_a_phrase()
    {
        var session = new FakeEngine();
        var tab = new BrowserTab(TabId.New());
        var useCase = new NavigateFromAddressBar(new FakeSearch("https://example.test/search?q="));

        Assert.True(useCase.Execute(tab, session, "aphelion browser"));
        Assert.Contains("aphelion", tab.Address?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("aphelion browser", tab.SearchTerm);
    }

    private sealed class FakeSearch(string prefix) : ISearchQueryBuilder
    {
        public PageAddress BuildSearchAddress(string searchTerm)
        {
            var uri = new Uri(prefix + Uri.EscapeDataString(searchTerm));
            Assert.True(PageAddress.TryCreate(uri, out var address));
            return address!;
        }
    }

    private sealed class FakeEngine : IBrowserEngineSession
    {
        public PageAddress? LastNavigated { get; private set; }

        public bool CanGoBack => false;

        public bool CanGoForward => false;

        public double SetZoomFactor(double factor) => factor;

        public void Navigate(PageAddress address) => LastNavigated = address;

        public bool GoBack() => false;

        public bool GoForward() => false;

        public bool Reload() => false;

        public bool StopLoading() => false;

        public void SetForeground(bool isForeground) => IsForeground = isForeground;

        public bool IsForeground { get; private set; } = true;

        public Task ClearBrowsingDataAsync() => Task.CompletedTask;

        public Task<string?> EvaluateAsync(string script) => Task.FromResult<string?>(null);

        public Task<EngineFindResult?> FindAsync(string text, bool forward, bool matchCase) =>
            Task.FromResult<EngineFindResult?>(null);

        public Task StopFindAsync() => Task.CompletedTask;

        public Task PrintAsync() => Task.CompletedTask;

        public bool OpenDevTools() => false;

        public Task SetMutedAsync(bool muted) => Task.CompletedTask;

        public IEngineDownloadOperation StartDownload(Uri source, string filePath) =>
            throw new NotSupportedException();

        public event EventHandler<EngineNavigationStartedEventArgs>? NavigationStarted
        {
            add { }
            remove { }
        }

        public event EventHandler<EngineNavigationCompletedEventArgs>? NavigationCompleted
        {
            add { }
            remove { }
        }

        public event EventHandler<EngineZoomFactorChangedEventArgs>? ZoomFactorChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<EngineDownloadStartedEventArgs>? DownloadStarted
        {
            add { }
            remove { }
        }

        public event EventHandler<EngineFullScreenElementChangedEventArgs>? FullScreenElementChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<EngineAcceleratorKeyPressedEventArgs>? AcceleratorKeyPressed
        {
            add { }
            remove { }
        }

        public event EventHandler<EnginePageMessageEventArgs>? PageMessage
        {
            add { }
            remove { }
        }
    }
}
