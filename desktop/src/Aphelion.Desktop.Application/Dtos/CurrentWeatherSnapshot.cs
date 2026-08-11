namespace Aphelion.Desktop.Application.Dtos;

/// <summary>Current conditions resolved for an approximate locality.</summary>
public sealed record CurrentWeatherSnapshot(
    string Location,
    double TemperatureCelsius,
    int WeatherCode);
