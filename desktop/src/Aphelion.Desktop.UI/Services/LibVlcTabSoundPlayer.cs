using Aphelion.Desktop.Application.Ports;
using LibVLCSharp.Shared;

namespace Aphelion.Desktop.UI.Services;

/// <summary>Cross-platform adapter for the bundled tab-open sound.</summary>
/// <remarks>
/// Only Windows and macOS native LibVLC binaries are bundled (see
/// <c>Aphelion.Desktop.UI.csproj</c>); Linux has none. The constructor can
/// therefore throw on Linux, which is why <c>CompositionRoot</c> never
/// registers this type directly — it tries to construct one and falls back to
/// <see cref="NullTabSoundPlayer"/> on failure, so a missing audio backend
/// costs a sound effect rather than the whole application.
/// </remarks>
internal sealed class LibVlcTabSoundPlayer : ITabSoundPlayer, IDisposable
{
    private readonly LibVLC _engine;
    private readonly MediaPlayer _player;
    private readonly Media _tabOpened;
    private readonly Media _tabClosed;
    private readonly Media _splash;
    private bool _loopSplash;

    public TimeSpan SplashDuration { get; }

    public LibVlcTabSoundPlayer()
    {
        Core.Initialize();
        _engine = new LibVLC("--no-video", "--quiet");
        _player = new MediaPlayer(_engine);
        _tabOpened = new Media(_engine, Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "0812.mp3"));
        _tabClosed = new Media(_engine, Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "0813.mp3"));
        _splash = new Media(_engine, Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "splash.mp3"));

        try
        {
            _splash.Parse(MediaParseOptions.ParseLocal, timeout: 2000).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Duration falls back below; the cue can still play when the window opens.
        }

        SplashDuration = _splash.Duration > 0
            ? TimeSpan.FromMilliseconds(_splash.Duration)
            : NullTabSoundPlayer.FallbackSplashDuration;

        // LibVLC forbids Play from inside EndReached; bounce to the pool first.
        _player.EndReached += (_, _) =>
        {
            if (!_loopSplash)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(_ => Play(_splash));
        };
    }

    public void PlayTabOpened()
    {
        _loopSplash = false;
        Play(_tabOpened);
    }

    public void PlayTabClosed()
    {
        _loopSplash = false;
        Play(_tabClosed);
    }

    public void PlaySplash(bool repeat = false)
    {
        _loopSplash = repeat;
        Play(_splash);
    }

    public void StopSplash()
    {
        _loopSplash = false;

        try
        {
            _player.Stop();
        }
        catch (Exception)
        {
            // Closing the splash must not fail because audio already ended.
        }
    }

    private void Play(Media sound)
    {
        try
        {
            _player.Stop();
            _player.Play(sound);
        }
        catch (Exception)
        {
            // Sound feedback is optional; it must never block tab creation.
        }
    }

    public void Dispose()
    {
        _tabOpened.Dispose();
        _tabClosed.Dispose();
        _splash.Dispose();
        _player.Dispose();
        _engine.Dispose();
    }
}
