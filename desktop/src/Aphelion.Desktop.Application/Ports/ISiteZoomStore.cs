namespace Aphelion.Desktop.Application.Ports;

/// <summary>Persists the chosen zoom percentage for one web origin.</summary>
public interface ISiteZoomStore
{
    int? Load(string origin);

    void Save(string origin, int percent);
}
