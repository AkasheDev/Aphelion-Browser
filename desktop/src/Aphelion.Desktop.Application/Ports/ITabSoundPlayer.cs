namespace Aphelion.Desktop.Application.Ports;

/// <summary>Plays short, non-blocking browser interaction sounds.</summary>
public interface ITabSoundPlayer
{
    void PlayTabOpened();

    void PlayTabClosed();

    /// <summary>
    /// Length of the splash cue. The orbit is timed to this so the body arrives
    /// home as the sound ends. A missing audio backend still reports a duration,
    /// so the scene is not skipped when the player could not start.
    /// </summary>
    TimeSpan SplashDuration { get; }

    /// <summary>
    /// Starts the splash cue. Called the moment the splash window is on screen,
    /// and stopped when that window closes so it never leaks into the browser.
    /// Pass <paramref name="repeat"/> for the --splash preview, so the cue repeats
    /// with the orbit instead of playing once.
    /// </summary>
    void PlaySplash(bool repeat = false);

    /// <summary>Stops the splash cue if it is still playing.</summary>
    void StopSplash();
}
