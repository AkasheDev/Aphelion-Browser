using Aphelion.Desktop.Domain;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.Enums;
using Aphelion.Desktop.Domain.ValueObjects;
using Aphelion.Desktop.UI.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

public sealed partial class ShellViewModel
{
    private sealed record ClosedTabRecord(
        string? Address,
        InternalPageKind? InternalPage,
        bool IsPinned,
        string? Title,
        string? SearchTerm);

    [RelayCommand]
    private void OpenSettingsPage() => OpenOrFocusInternal(InternalPageKind.Settings);

    [RelayCommand]
    private void OpenHistoryPage() => OpenOrFocusInternal(InternalPageKind.History);

    [RelayCommand]
    private void ToggleCommandPalette() => IsCommandPaletteOpen = !IsCommandPaletteOpen;

    [RelayCommand]
    private void CloseCommandPalette() => IsCommandPaletteOpen = false;

    [RelayCommand]
    private void FindInPage() => FocusedBrowser?.OpenFindBarCommand.Execute(null);

    [RelayCommand]
    private void ReloadActive() => FocusedBrowser?.ReloadCommand.Execute(null);

    [RelayCommand]
    private void PrintActive() => FocusedBrowser?.PrintPageCommand.Execute(null);

    [RelayCommand]
    private void OpenDevTools() => FocusedBrowser?.OpenDevToolsCommand.Execute(null);

    [RelayCommand]
    private void PinTab(TabItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.Tab.IsPinned)
        {
            _session.UnpinTab(item.Id);
        }
        else
        {
            _session.PinTab(item.Id);
        }

        SyncTabs();
    }

    [RelayCommand]
    private async Task MuteTab(TabItemViewModel? item)
    {
        if (item is null || !_browsers.TryGetValue(item.Id, out var browser))
        {
            return;
        }

        item.Tab.SetMuted(!item.Tab.IsMuted);
        await browser.ApplyMuteAsync().ConfigureAwait(true);
        SyncTabs();
    }

    [RelayCommand]
    private void DuplicateTab(TabItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var copy = _session.OpenTabNextTo(item.Tab, activate: true);
        Attach(copy);

        if (item.Tab.InternalPage is { } page)
        {
            _browsers[copy.Id].ShowInternal(page, recordHistory: false);
        }
        else if (item.Tab.Address is { } address)
        {
            _browsers[copy.Id].NavigateTo(address);
        }

        SyncTabs();
    }

    [RelayCommand]
    private async Task CopyTabUrl(TabItemViewModel? item)
    {
        var text = item is null
            ? null
            : item.Tab.InternalPage is { } page
                ? InternalPages.AddressOf(page)
                : item.Tab.Address?.ToString();

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.Windows.OfType<MainWindow>().FirstOrDefault(window =>
                window.DataContext is MainWindowViewModel { Shell: { } shell } &&
                ReferenceEquals(shell, this)) is { Clipboard: { } clipboard })
        {
            await clipboard.SetTextAsync(text).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void ReopenClosedTab()
    {
        if (_closedTabs.Count == 0)
        {
            return;
        }

        var closed = _closedTabs.Pop();
        OnPropertyChanged(nameof(CanReopenClosedTab));
        var tab = _session.OpenTab();
        Attach(tab);

        if (closed.InternalPage is { } page)
        {
            _browsers[tab.Id].ShowInternal(page, recordHistory: false);
        }
        else if (closed.Address is not null &&
                 Uri.TryCreate(closed.Address, UriKind.Absolute, out var uri) &&
                 PageAddress.TryCreate(uri, out var address) &&
                 address is not null)
        {
            _browsers[tab.Id].NavigateTo(address);
        }

        if (closed.IsPinned)
        {
            _session.PinTab(tab.Id);
        }

        SyncTabs();
    }

    private void OnOpenInNewTabRequested(object? sender, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            PageAddress.TryCreate(uri, out var address) &&
            address is not null)
        {
            OpenInNewTab(address, activate: false);
        }
    }

    private void OnSearchRequested(object? sender, string query) =>
        FocusedBrowser?.NavigateFromOmnibox(query);

    private async void OnCopyTextRequested(object? sender, string text) =>
        await CopyTextAsync(text).ConfigureAwait(true);

    private async Task CopyTextAsync(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = desktop.Windows.OfType<MainWindow>().FirstOrDefault(candidate =>
                candidate.DataContext is MainWindowViewModel { Shell: { } shell } &&
                ReferenceEquals(shell, this));
            if (window?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text).ConfigureAwait(true);
            }
        }
    }

    public bool CanReopenClosedTab => _closedTabs.Count > 0;

    private void OpenOrFocusInternal(InternalPageKind kind)
    {
        IsDownloadsBubbleOpen = false;
        IsCommandPaletteOpen = false;

        var existing = _session.Tabs.FirstOrDefault(tab =>
            tab.InternalPage == kind && !tab.IsSplitPartner);

        if (existing is not null)
        {
            if (existing.GroupId is { } groupId &&
                _session.FindGroup(groupId) is { IsCollapsed: true } hidden)
            {
                RevealGroup(hidden);
            }

            _session.Activate(existing.Id);
            SyncTabs();
            return;
        }

        var tab = _session.OpenTab();
        _tabSounds?.PlayTabOpened();
        Attach(tab);
        _browsers[tab.Id].ShowInternal(kind, recordHistory: false);
        var item = ItemFor(tab);
        item.IsExiting = true;
        SyncTabs();
        RevealAfterRender(item);
    }

    private bool OpenInitialTabs()
    {
        if (IsPrivateWindow)
        {
            return false;
        }

        var startup = _appSettings?.Load().Startup ?? StartupMode.RestoreSession;

        if (startup == StartupMode.NewTab)
        {
            return false;
        }

        if (startup == StartupMode.CustomPages)
        {
            var pages = _appSettings?.Load().StartupPages ?? [];
            var opened = false;
            foreach (var page in pages)
            {
                if (InternalPages.TryMatch(page, out var kind))
                {
                    var tab = opened ? _session.OpenTab(activate: false) : _session.OpenTab();
                    Attach(tab);
                    _browsers[tab.Id].ShowInternal(kind, recordHistory: false);
                    opened = true;
                    continue;
                }

                if (Uri.TryCreate(page, UriKind.Absolute, out var uri) &&
                    PageAddress.TryCreate(uri, out var address) &&
                    address is not null)
                {
                    var tab = opened ? _session.OpenTab(address, activate: false) : _session.OpenTab(address);
                    Attach(tab);
                    opened = true;
                }
            }

            if (opened)
            {
                SyncTabs();
                return true;
            }

            return false;
        }

        return TryRestore();
    }

    internal void RememberClosed(BrowserTab tab)
    {
        _closedTabs.Push(new ClosedTabRecord(
            tab.Address?.ToString(),
            tab.InternalPage,
            tab.IsPinned,
            tab.Title,
            tab.SearchTerm));

        while (_closedTabs.Count > 25)
        {
            var buffer = _closedTabs.Reverse().Skip(1).Reverse().ToList();
            _closedTabs.Clear();
            foreach (var item in buffer)
            {
                _closedTabs.Push(item);
            }
        }

        OnPropertyChanged(nameof(CanReopenClosedTab));
    }
}
