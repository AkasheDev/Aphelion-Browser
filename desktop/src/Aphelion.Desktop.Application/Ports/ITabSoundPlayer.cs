namespace Aphelion.Desktop.Application.Ports;

/// <summary>Plays short, non-blocking browser interaction sounds.</summary>
public interface ITabSoundPlayer
{
    void PlayTabOpened();

    void PlayTabClosed();
}
