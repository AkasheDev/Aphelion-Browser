using Aphelion.Desktop.Domain.Enums;

namespace Aphelion.Desktop.Application.Ports;

public interface ISearchEnginePreferenceStore
{
    SearchEngineKind? Load();

    void Save(SearchEngineKind engine);
}
