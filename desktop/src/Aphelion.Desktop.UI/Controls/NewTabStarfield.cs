using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aphelion.Desktop.UI.Controls;

/// <summary>
/// A deterministic, resolution-independent deep star field for the New Tab page.
/// It owns animation and drawing only; layout and product interaction remain in
/// the page view model and view.
/// </summary>
public sealed class NewTabStarfield : Control
{
    private const int StarCount = 150;
    private const double Tau = Math.PI * 2;

    public static readonly StyledProperty<IBrush?> StarBrushProperty =
        AvaloniaProperty.Register<NewTabStarfield, IBrush?>(nameof(StarBrush));

    public static readonly StyledProperty<IBrush?> CyanBrushProperty =
        AvaloniaProperty.Register<NewTabStarfield, IBrush?>(nameof(CyanBrush));

    public static readonly StyledProperty<IBrush?> VioletBrushProperty =
        AvaloniaProperty.Register<NewTabStarfield, IBrush?>(nameof(VioletBrush));

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Star[] _stars = CreateStars();
    private readonly Meteor[] _meteors =
    [
        new(0.12, 0.18, 0.51, 0.43, 10.7, 1.4, 0.10, 1.25, Accent.Cyan),
        new(0.58, 0.10, 0.88, 0.32, 14.8, 6.2, 0.075, 0.95, Accent.White),
        new(0.30, 0.55, 0.62, 0.78, 18.5, 11.0, 0.065, 0.8, Accent.Violet),
    ];

    static NewTabStarfield()
    {
        AffectsRender<NewTabStarfield>(StarBrushProperty, CyanBrushProperty, VioletBrushProperty);
    }

    public NewTabStarfield() => _timer.Tick += (_, _) => InvalidateVisual();

    public IBrush? StarBrush
    {
        get => GetValue(StarBrushProperty);
        set => SetValue(StarBrushProperty, value);
    }

    public IBrush? CyanBrush
    {
        get => GetValue(CyanBrushProperty);
        set => SetValue(CyanBrushProperty, value);
    }

    public IBrush? VioletBrush
    {
        get => GetValue(VioletBrushProperty);
        set => SetValue(VioletBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Bounds.Width <= 0 || Bounds.Height <= 0 || StarBrush is null)
        {
            return;
        }

        var seconds = _clock.Elapsed.TotalSeconds;
        DrawStars(context, seconds);
        DrawMeteors(context, seconds);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _clock.Restart();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void DrawStars(DrawingContext context, double seconds)
    {
        foreach (var star in _stars)
        {
            var shimmer = 0.5 + (0.5 * Math.Sin(star.Phase + (seconds * star.Speed)));
            var opacity = star.BaseOpacity * (0.28 + (0.72 * shimmer * shimmer));
            var point = new Point(star.X * Bounds.Width, star.Y * Bounds.Height);
            var brush = star.Accent switch
            {
                Accent.Cyan => CyanBrush ?? StarBrush,
                Accent.Violet => VioletBrush ?? StarBrush,
                _ => StarBrush,
            };

            using (context.PushOpacity(opacity * 0.12))
            {
                context.DrawEllipse(brush, null, point, star.Radius * 3.6, star.Radius * 3.6);
            }

            using (context.PushOpacity(opacity))
            {
                context.DrawEllipse(brush, null, point, star.Radius, star.Radius);

                if (star.Radius > 1.35)
                {
                    context.DrawLine(
                        new Pen(brush, 0.55),
                        new Point(point.X - (star.Radius * 2.6), point.Y),
                        new Point(point.X + (star.Radius * 2.6), point.Y));
                    context.DrawLine(
                        new Pen(brush, 0.55),
                        new Point(point.X, point.Y - (star.Radius * 2.6)),
                        new Point(point.X, point.Y + (star.Radius * 2.6)));
                }
            }
        }
    }

    private void DrawMeteors(DrawingContext context, double seconds)
    {
        foreach (var meteor in _meteors)
        {
            var cycleTime = PositiveModulo(seconds - meteor.Delay, meteor.Cycle);

            if (seconds < meteor.Delay || cycleTime > meteor.Cycle * meteor.ActiveFraction)
            {
                continue;
            }

            var progress = cycleTime / (meteor.Cycle * meteor.ActiveFraction);
            var eased = progress * progress * (3 - (2 * progress));
            var fade = Math.Sin(progress * Math.PI);
            var start = new Point(meteor.StartX * Bounds.Width, meteor.StartY * Bounds.Height);
            var end = new Point(meteor.EndX * Bounds.Width, meteor.EndY * Bounds.Height);
            var head = Lerp(start, end, eased);
            var direction = new Vector(end.X - start.X, end.Y - start.Y);
            var length = Math.Max(1, Math.Sqrt(
                (direction.X * direction.X) + (direction.Y * direction.Y)));
            var unit = direction / length;
            var brush = meteor.Accent switch
            {
                Accent.Cyan => CyanBrush ?? StarBrush,
                Accent.Violet => VioletBrush ?? StarBrush,
                _ => StarBrush,
            };

            for (var segment = 5; segment >= 1; segment--)
            {
                var near = head - (unit * (segment - 1) * 24);
                var far = head - (unit * segment * 24);

                using (context.PushOpacity(fade * (0.05 + ((6 - segment) * 0.055))))
                {
                    context.DrawLine(new Pen(brush, meteor.Thickness), far, near);
                }
            }

            using (context.PushOpacity(fade * 0.85))
            {
                context.DrawEllipse(brush, null, head, meteor.Thickness * 1.5, meteor.Thickness * 1.5);
            }
        }
    }

    private static Star[] CreateStars()
    {
        var random = new Random(0xA9E110);
        var stars = new Star[StarCount];

        for (var index = 0; index < stars.Length; index++)
        {
            var depth = random.NextDouble();
            var radius = 0.45 + (Math.Pow(depth, 2.8) * 1.75);
            var accentRoll = random.NextDouble();
            var accent = accentRoll < 0.07
                ? Accent.Cyan
                : accentRoll > 0.95
                    ? Accent.Violet
                    : Accent.White;

            stars[index] = new Star(
                random.NextDouble(),
                random.NextDouble(),
                radius,
                random.NextDouble() * Tau,
                0.45 + (random.NextDouble() * 1.45),
                0.22 + (depth * 0.56),
                accent);
        }

        return stars;
    }

    private static Point Lerp(Point start, Point end, double amount) =>
        new(
            start.X + ((end.X - start.X) * amount),
            start.Y + ((end.Y - start.Y) * amount));

    private static double PositiveModulo(double value, double modulus) =>
        ((value % modulus) + modulus) % modulus;

    private enum Accent
    {
        White,
        Cyan,
        Violet,
    }

    private readonly record struct Star(
        double X,
        double Y,
        double Radius,
        double Phase,
        double Speed,
        double BaseOpacity,
        Accent Accent);

    private readonly record struct Meteor(
        double StartX,
        double StartY,
        double EndX,
        double EndY,
        double Cycle,
        double Delay,
        double ActiveFraction,
        double Thickness,
        Accent Accent);
}
