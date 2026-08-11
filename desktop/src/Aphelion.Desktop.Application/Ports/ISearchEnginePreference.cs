using Aphelion.Desktop.Domain.Enums;

namespace Aphelion.Desktop.Application.Ports;

public interface ISearchEnginePreference
{
    SearchEngineKind Selected { get; }

    event EventHandler? Changed;

    void ChangeTo(SearchEngineKind engine);
}
