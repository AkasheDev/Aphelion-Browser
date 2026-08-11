using System.Globalization;
using System.Text.Json;
using Aphelion.Desktop.Application.Dtos;
using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Network;

/// <summary>
/// Resolves an approximate city from the public IP, then requests current
/// conditions from Open-Meteo. No precise device location or API key is used.
/// </summary>
public sealed class OpenMeteoCurrentWeatherProvider : ICurrentWeatherProvider, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public async Task<CurrentWeatherSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var locationResponse = await _http.GetAsync(
                "https://ipwho.is/",
                cancellationToken).ConfigureAwait(false);
            locationResponse.EnsureSuccessStatusCode();

            await using var locationStream = await locationResponse.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var location = await JsonDocument.ParseAsync(
                locationStream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = location.RootElement;

            if (!root.TryGetProperty("success", out var success) || !success.GetBoolean())
            {
                return null;
            }

            var city = root.GetProperty("city").GetString();
            var latitude = root.GetProperty("latitude").GetDouble();
            var longitude = root.GetProperty("longitude").GetDouble();

            if (string.IsNullOrWhiteSpace(city))
            {
                return null;
            }

            var weatherUri = string.Create(
                CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={latitude:F4}&longitude={longitude:F4}&current=temperature_2m,weather_code&timezone=auto");
            using var weatherResponse = await _http.GetAsync(weatherUri, cancellationToken)
                .ConfigureAwait(false);
            weatherResponse.EnsureSuccessStatusCode();

            await using var weatherStream = await weatherResponse.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var weather = await JsonDocument.ParseAsync(
                weatherStream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var current = weather.RootElement.GetProperty("current");

            return new CurrentWeatherSnapshot(
                city.Trim(),
                current.GetProperty("temperature_2m").GetDouble(),
                current.GetProperty("weather_code").GetInt32());
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or JsonException
                or InvalidOperationException
                or KeyNotFoundException
                or FormatException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}
