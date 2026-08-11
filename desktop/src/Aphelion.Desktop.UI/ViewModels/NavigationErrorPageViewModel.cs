using Aphelion.Desktop.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// Presentation state for the local page shown when a navigation cannot finish.
/// Platform engines report failure detail inconsistently, so classification is
/// deliberately conservative and always falls back to a truthful generic page.
/// </summary>
public sealed partial class NavigationErrorPageViewModel : ViewModelBase
{
    private readonly Action _retry;
    private readonly Action _openNewTab;

    public NavigationErrorPageViewModel(Action retry, Action openNewTab)
    {
        _retry = retry ?? throw new ArgumentNullException(nameof(retry));
        _openNewTab = openNewTab ?? throw new ArgumentNullException(nameof(openNewTab));
    }

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _headline = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _hint = string.Empty;

    [ObservableProperty]
    private string _addressLabel = string.Empty;

    public void Present(PageAddress? address, string? engineReason)
    {
        AddressLabel = address?.DisplayHost ?? "this address";

        var reason = engineReason?.ToUpperInvariant() ?? string.Empty;

        if (reason.Contains("DNS", StringComparison.Ordinal) ||
            reason.Contains("NAME_NOT_RESOLVED", StringComparison.Ordinal) ||
            reason.Contains("HOST NOT FOUND", StringComparison.Ordinal))
        {
            Headline = "This address couldn’t be found";
            Message = $"Aphelion couldn’t find {AddressLabel}.";
            Hint = "Check the spelling of the address or your DNS connection.";
        }
        else if (reason.Contains("OFFLINE", StringComparison.Ordinal) ||
                 reason.Contains("NETWORK UNREACHABLE", StringComparison.Ordinal) ||
                 reason.Contains("NO INTERNET", StringComparison.Ordinal))
        {
            Headline = "You appear to be offline";
            Message = $"Aphelion couldn’t connect to {AddressLabel}.";
            Hint = "Check your connection, then try again.";
        }
        else
        {
            Headline = "This page couldn’t be loaded";
            Message = $"Aphelion couldn’t reach {AddressLabel}.";
            Hint = "The site may be unavailable, or your connection may need attention.";
        }

        IsVisible = true;
    }

    public void Hide() => IsVisible = false;

    [RelayCommand]
    private void Retry() => _retry();

    [RelayCommand]
    private void OpenNewTab() => _openNewTab();
}
