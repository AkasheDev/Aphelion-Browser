using System.Collections.ObjectModel;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// The window shell: the tab strip in the title bar, the side panel, and which
/// browser view is on screen.
/// </summary>
/// <remarks>
/// Every tab owns its own <see cref="BrowserViewModel"/>, because every tab needs
/// its own engine session and navigation history. The shell keeps them alive and
/// swaps which one is visible; it does not reuse a single view across tabs, which
/// would lose per-tab history.
/// </remarks>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly BrowsingSession _session = new();
    private readonly Func<BrowserViewModel> _browserFactory;
    private readonly Dictionary<TabId, BrowserViewModel> _browsers = [];

    public ShellViewModel(Func<BrowserViewModel> browserFactory)
    {
        _browserFactory = browserFactory ?? throw new ArgumentNullException(nameof(browserFactory));

        NewTab();
    }

    public ObservableCollection<TabItemViewModel> Tabs { get; } = [];

    [ObservableProperty]
    private TabItemViewModel? _activeTab;

    /// <summary>The browser view for the active tab, shown in the content area.</summary>
    [ObservableProperty]
    private BrowserViewModel? _activeBrowser;

    [ObservableProperty]
    private string _windowTitle = "Aphelion";

    [RelayCommand]
    private void NewTab()
    {
        var tab = _session.OpenTab();
        Attach(tab);
        SyncTabs();
    }

    [RelayCommand]
    private void CloseTab(TabItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (_browsers.Remove(item.Id, out var browser))
        {
            browser.PropertyChanged -= OnBrowserPropertyChanged;
        }

        _session.CloseTab(item.Id);

        // Closing the last tab opens a fresh one rather than leaving an empty
        // window: a browser with no tabs has nothing to show and no way back.
        if (_session.IsEmpty)
        {
            Attach(_session.OpenTab());
        }

        SyncTabs();
    }

    [RelayCommand]
    private void ActivateTab(TabItemViewModel? item)
    {
        if (item is null || !_session.Activate(item.Id))
        {
            return;
        }

        SyncTabs();
    }

    /// <summary>Groups the active tab, creating a group if it has none.</summary>
    [RelayCommand]
    private void GroupActiveTab()
    {
        if (_session.ActiveTab is not { } tab)
        {
            return;
        }

        if (tab.GroupId is not null)
        {
            _session.RemoveFromGroup(tab.Id);
        }
        else
        {
            var color = NextGroupColor();
            var group = _session.CreateGroup($"Group {_session.Groups.Count + 1}", color);
            _session.AddToGroup(tab.Id, group.Id);
        }

        SyncTabs();
    }

    /// <summary>Cycles the palette so consecutive groups are visually distinct.</summary>
    private GroupColor NextGroupColor()
    {
        var palette = Enum.GetValues<GroupColor>();
        return palette[_session.Groups.Count % palette.Length];
    }

    private void Attach(BrowserTab tab)
    {
        var browser = _browserFactory();
        browser.Bind(tab);
        browser.PropertyChanged += OnBrowserPropertyChanged;
        _browsers[tab.Id] = browser;
    }

    private void OnBrowserPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // A tab's title and loading state change as pages load, so the strip has to
        // follow rather than only refreshing when tabs open and close.
        if (e.PropertyName is nameof(BrowserViewModel.IsLoading)
            or nameof(BrowserViewModel.PageTitle))
        {
            RefreshTabDisplay();
        }
    }

    /// <summary>Rebuilds the strip from the session and updates the visible browser.</summary>
    private void SyncTabs()
    {
        Tabs.Clear();

        foreach (var tab in _session.Tabs)
        {
            var item = new TabItemViewModel(tab)
            {
                IsActive = _session.ActiveTab?.Id == tab.Id,
            };

            item.Refresh(GroupColorOf(tab));
            Tabs.Add(item);
        }

        ActiveTab = Tabs.FirstOrDefault(t => t.IsActive);
        ActiveBrowser = ActiveTab is null ? null : _browsers.GetValueOrDefault(ActiveTab.Id);

        WindowTitle = ActiveTab is null ? "Aphelion" : $"{ActiveTab.Title} — Aphelion";
    }

    private void RefreshTabDisplay()
    {
        foreach (var item in Tabs)
        {
            item.Refresh(GroupColorOf(item.Tab));
        }

        if (ActiveTab is not null)
        {
            WindowTitle = $"{ActiveTab.Title} — Aphelion";
        }
    }

    private GroupColor? GroupColorOf(BrowserTab tab) =>
        tab.GroupId is { } id ? _session.FindGroup(id)?.Color : null;
}
