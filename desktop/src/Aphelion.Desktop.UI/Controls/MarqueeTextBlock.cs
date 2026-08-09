using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Aphelion.Desktop.UI.Controls;

/// <summary>
/// Text that scrolls itself when it does not fit, and only while
/// <see cref="IsScrollEnabled"/> is set.
/// </summary>
/// <remarks>
/// Used for tab titles: the active tab reveals a long title by scrolling it,
/// while inactive tabs stay still and ellipsised. Constant motion across every
/// tab would be unreadable, so scrolling is gated on the tab being active.
/// </remarks>
public sealed class MarqueeTextBlock : TemplatedControl, IDisposable
{
    /// <summary>Pixels travelled per second.</summary>
    private const double Speed = 30;

    /// <summary>Pause at each end before reversing, so the text can be read.</summary>
    private static readonly TimeSpan EdgePause = TimeSpan.FromSeconds(1.4);

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, string?>(nameof(Text));

    public static readonly StyledProperty<bool> IsScrollEnabledProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, bool>(nameof(IsScrollEnabled));

    private TextBlock? _text;
    private CancellationTokenSource? _loop;

    static MarqueeTextBlock()
    {
        ClipToBoundsProperty.OverrideDefaultValue<MarqueeTextBlock>(true);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Whether the text may scroll. False keeps it still and trimmed.</summary>
    public bool IsScrollEnabled
    {
        get => GetValue(IsScrollEnabledProperty);
        set => SetValue(IsScrollEnabledProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _text = e.NameScope.Find<TextBlock>("PART_Text");
        Restart();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty ||
            change.Property == IsScrollEnabledProperty ||
            change.Property == BoundsProperty)
        {
            Restart();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Stop();
    }

    /// <summary>Stops the animation loop and releases its cancellation source.</summary>
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void Stop()
    {
        _loop?.Cancel();
        _loop?.Dispose();
        _loop = null;

        if (_text is not null)
        {
            _text.RenderTransform = null;
        }
    }

    private void Restart()
    {
        Stop();

        if (_text is null || !IsScrollEnabled || Bounds.Width <= 0)
        {
            return;
        }

        // Measure the text unconstrained: the overflow is what has to scroll.
        _text.Measure(new Size(double.PositiveInfinity, Bounds.Height));
        var overflow = _text.DesiredSize.Width - Bounds.Width;

        if (overflow <= 1)
        {
            return;
        }

        _loop = new CancellationTokenSource();
        _ = RunAsync(_text, overflow, _loop.Token);
    }

    /// <summary>
    /// Slides the text to its far end and back, pausing at each edge, until the
    /// control stops scrolling or leaves the tree.
    /// </summary>
    private static async Task RunAsync(TextBlock text, double overflow, CancellationToken token)
    {
        var duration = TimeSpan.FromSeconds(overflow / Speed);

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(EdgePause, token).ConfigureAwait(true);
                await Slide(text, 0, -overflow, duration, token).ConfigureAwait(true);

                await Task.Delay(EdgePause, token).ConfigureAwait(true);
                await Slide(text, -overflow, 0, duration, token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected whenever the tab is deactivated or the title changes.
        }
    }

    private static Task Slide(TextBlock text, double from, double to, TimeSpan duration, CancellationToken token) =>
        new Animation
        {
            Duration = duration,
            Easing = new LinearEasing(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(TranslateTransform.XProperty, from) },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(TranslateTransform.XProperty, to) },
                },
            },
        }.RunAsync(text, token);
}
