using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.UI.Services;

/// <summary>
/// Does nothing. Used when the real sound player's native engine could not be
/// initialized, so a missing audio backend degrades to silence rather than
/// stopping the browser from starting.
/// </summary>
internal sealed class NullTabSoundPlayer : ITabSoundPlayer
{
    /// <summary>
    /// Used when LibVLC could not report the cue's length. Long enough that the
    /// orbit is visible; the real player replaces this with the file's duration.
    /// </summary>
    internal static readonly TimeSpan FallbackSplashDuration = TimeSpan.FromSeconds(8);

    public TimeSpan SplashDuration => FallbackSplashDuration;

    public void PlayTabOpened()
    {
    }

    public void PlayTabClosed()
    {
    }

    public void PlaySplash(bool repeat = false)
    {
    }

    public void StopSplash()
    {
    }
}
