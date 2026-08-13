using Aphelion.Desktop.Application.Ports;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>Process-wide time greeting and current-weather presentation state.</summary>
public sealed partial class NewTabAmbientViewModel : ViewModelBase
{
    private static readonly TimeSpan WeatherRefreshInterval = TimeSpan.FromMinutes(30);

    private readonly ICurrentWeatherProvider _weather;
    private readonly IPrivacyPreferenceStore _privacy;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMinutes(1) };
    private DateTimeOffset _lastWeatherRefresh = DateTimeOffset.MinValue;

    /// <summary>
    /// True for the instance a private window builds for itself rather than
    /// sharing the process-wide one. Weather is off by default here — see
    /// <see cref="IsWeatherEnabled"/> — and the choice is never read from or
    /// written to <see cref="IPrivacyPreferenceStore"/>, so turning it on inside
    /// a private window neither reveals nor is revealed by the ordinary
    /// preference, and disappears when the window closes.
    /// </summary>
    private readonly bool _isPrivateInstance;

    public NewTabAmbientViewModel(
        ICurrentWeatherProvider weather,
        IPrivacyPreferenceStore privacy,
        bool isPrivateInstance = false)
    {
        _weather = weather ?? throw new ArgumentNullException(nameof(weather));
        _privacy = privacy ?? throw new ArgumentNullException(nameof(privacy));
        _isPrivateInstance = isPrivateInstance;
        _isWeatherEnabled = !isPrivateInstance && _privacy.Load().WeatherEnabled;
        UpdateGreeting();
        _clock.Tick += OnClockTick;
        _clock.Start();

        if (_isWeatherEnabled)
        {
            RefreshWeatherCommand.Execute(null);
        }
    }

    /// <summary>
    /// Whether the weather card is allowed to resolve an approximate location
    /// from the public IP and query current conditions for it. On by default,
    /// matching what shipped; off stops every future request, including the
    /// periodic refresh, until turned back on.
    /// </summary>
    [ObservableProperty]
    private bool _isWeatherEnabled;

    partial void OnIsWeatherEnabledChanged(bool value)
    {
        // A private window's choice for this session only — see
        // _isPrivateInstance — must never touch the shared preference file.
        if (!_isPrivateInstance)
        {
            _privacy.Save(_privacy.Load() with { WeatherEnabled = value });
        }

        if (value)
        {
            RefreshWeatherCommand.Execute(null);
        }
        else
        {
            WeatherLocation = "Weather";
            TemperatureText = "--°";
            WeatherDescription = "Weather is turned off";
        }
    }

    [RelayCommand]
    private void ToggleWeather() => IsWeatherEnabled = !IsWeatherEnabled;

    [ObservableProperty]
    private string _greetingHeadline = string.Empty;

    [ObservableProperty]
    private string _greetingPrompt = string.Empty;

    [ObservableProperty]
    private string _weatherLocation = "Weather";

    [ObservableProperty]
    private string _temperatureText = "--°";

    [ObservableProperty]
    private string _weatherDescription = "Current weather unavailable";

    [ObservableProperty]
    private bool _isWeatherLoading;

    [RelayCommand]
    private async Task RefreshWeatherAsync()
    {
        if (IsWeatherLoading || !IsWeatherEnabled)
        {
            return;
        }

        IsWeatherLoading = true;

        try
        {
            var snapshot = await _weather.GetCurrentAsync().ConfigureAwait(true);
            _lastWeatherRefresh = DateTimeOffset.Now;

            if (snapshot is null)
            {
                WeatherDescription = "Weather unavailable — click to retry";
                return;
            }

            WeatherLocation = snapshot.Location;
            TemperatureText = $"{Math.Round(snapshot.TemperatureCelsius):0}°C";
            WeatherDescription = Describe(snapshot.WeatherCode);
        }
        finally
        {
            IsWeatherLoading = false;
        }
    }

    internal static GreetingCopy GreetingFor(int hour) => hour switch
    {
        < 5 => new("Still exploring?", "One more destination."),
        < 12 => new("Good morning.", "Where will today take you?"),
        < 17 => new("Good afternoon.", "What's next?"),
        < 22 => new("Good evening.", "Where to?"),
        _ => new("Still exploring?", "The night is yours."),
    };

    private void OnClockTick(object? sender, EventArgs e)
    {
        UpdateGreeting();

        if (DateTimeOffset.Now - _lastWeatherRefresh >= WeatherRefreshInterval)
        {
            RefreshWeatherCommand.Execute(null);
        }
    }

    private void UpdateGreeting()
    {
        var copy = GreetingFor(DateTime.Now.Hour);
        GreetingHeadline = copy.Headline;
        GreetingPrompt = copy.Prompt;
    }

    private static string Describe(int code) => code switch
    {
        0 => "Clear sky",
        1 or 2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Foggy",
        >= 51 and <= 67 => "Rainy",
        >= 71 and <= 77 => "Snowy",
        >= 80 and <= 82 => "Rain showers",
        >= 85 and <= 86 => "Snow showers",
        >= 95 => "Thunderstorms",
        _ => "Current weather",
    };

    internal readonly record struct GreetingCopy(string Headline, string Prompt);
}
