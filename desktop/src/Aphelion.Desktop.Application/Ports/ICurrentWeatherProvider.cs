using Aphelion.Desktop.Application.Dtos;

namespace Aphelion.Desktop.Application.Ports;

public interface ICurrentWeatherProvider
{
    Task<CurrentWeatherSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default);
}
