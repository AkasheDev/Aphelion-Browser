using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.UI.Services;

/// <summary>
/// Does nothing. Used when the real sound player's native engine could not be
/// initialized, so a missing audio backend degrades to silence rather than
/// stopping the browser from starting.
/// </summary>
internal sealed class NullTabSoundPlayer : ITabSoundPlayer
{
    public void PlayTabOpened()
    {
    }

    public void PlayTabClosed()
    {
    }
}
