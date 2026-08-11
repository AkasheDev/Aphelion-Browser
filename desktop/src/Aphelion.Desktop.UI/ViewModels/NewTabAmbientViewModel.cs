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
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMinutes(1) };
    private DateTimeOffset _lastWeatherRefresh = DateTimeOffset.MinValue;

    public NewTabAmbientViewModel(ICurrentWeatherProvider weather)
    {
        _weather = weather ?? throw new ArgumentNullException(nameof(weather));
        UpdateGreeting();
        _clock.Tick += OnClockTick;
        _clock.Start();
        RefreshWeatherCommand.Execute(null);
    }

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
        if (IsWeatherLoading)
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
