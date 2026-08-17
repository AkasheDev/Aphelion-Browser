using Aphelion.Desktop.Application.Dtos;

namespace Aphelion.Desktop.Application.Ports;

public interface IAppSettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);

    event EventHandler? Changed;
}
