using System.ComponentModel;
using Aphelion.Desktop.UI.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

// System.IO is in scope through the implicit usings, and it has a Path of its own.
using Path = Avalonia.Controls.Shapes.Path;

namespace Aphelion.Desktop.UI.Views;

/// <summary>
/// The orbit the splash screen is built around: a body sets out from the near
/// side and arrives at the aphelion — the point farthest from the star it
/// circles — exactly as startup finishes.
/// </summary>
/// <remarks>
/// The scene is in the XAML; the motion is here, because XAML can lay out a
/// scene but not walk a point around an ellipse. Everything is created once and
/// then only repositioned, so a frame costs a few dozen point assignments and no
/// allocation of controls.
/// </remarks>
public partial class SplashWindow : Window
{
    /// <summary>Centre of the system, in the content area inside the root border.</summary>
    private const double CentreX = 312;
    private const double CentreY = 178;

    /// <summary>Semi-axes of the orbit. Wide and shallow, as an orbit seen near its plane.</summary>
    private const double RadiusX = 246;
    private const double RadiusY = 112;

    /// <summary>Points used to draw the swept arc. Enough that no straight edge shows.</summary>
    private const int ArcResolution = 120;

    /// <summary>How much of the trail behind the body burns brighter than the rest.</summary>
    private const double HeadFraction = 0.16;

    /// <summary>
    /// How quickly the drawn progress catches up with the reported progress, per
    /// frame. Startup reports in jumps; gliding between them is the difference
    /// between a scene and a progress bar. High enough that the body is visibly
    /// travelling rather than drifting — a splash that dawdles reads as a slow
    /// application.
    /// </summary>
    private const double Smoothing = 0.19;

    /// <summary>
    /// The least the body advances per frame once it is behind, as a fraction of
    /// the whole journey. Exponential easing alone crawls over the last stretch of
    /// every step, which is exactly where it looks stuck.
    /// </summary>
    private const double MinimumStep = 0.0045;

    /// <summary>Size of the panel holding the star and its corona.</summary>
    private const double CentreSize = 260;

    private readonly Path _track = new();
    private readonly Path _sweptGlow = new();
    private readonly Path _swept = new();
    private readonly Path _headGlow = new();
    private readonly Path _head = new();
    private readonly Ellipse _aphelionRing = new();
    private readonly Ellipse _bodyHalo = new();
    private readonly Ellipse _body = new();

    private readonly DispatcherTimer _ticker;

    private SplashViewModel? _model;
    private double _target;
    private double _shown;

    public SplashWindow()
    {
        InitializeComponent();

        _ticker = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };

        _ticker.Tick += OnTick;

        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        BuildStarfield();
        BuildOrbit();
        PlaceCentre();

        Draw(0);

        if (this.FindControl<Border>("Root") is { } root)
        {
            root.Opacity = 1;
        }

        _ticker.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _ticker.Stop();
        _ticker.Tick -= OnTick;

        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
            _model = null;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = DataContext as SplashViewModel;

        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
            _target = _model.ProgressPercent / 100d;
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SplashViewModel.ProgressPercent) && _model is not null)
        {
            _target = Math.Clamp(_model.ProgressPercent / 100d, 0, 1);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var delta = _target - _shown;

        // Settled: stop redrawing a scene that is not moving. The animations
        // declared in XAML keep running on their own.
        if (Math.Abs(delta) < 0.0005)
        {
            if (_shown != _target)
            {
                _shown = _target;
                Draw(_shown);
            }

            return;
        }

        // The last leg closes faster. Startup shuts the window shortly after it
        // reports completion, so a body still ambling towards the aphelion would
        // be cut off just short of arriving — the one frame the whole scene is
        // built around.
        var rate = _target >= 1 ? Smoothing * 2.1 : Smoothing;
        var step = delta * rate;

        // Keep it moving through the tail of the ease, where the remaining
        // distance is small and pure easing would inch along.
        if (Math.Abs(step) < MinimumStep)
        {
            step = Math.Sign(delta) * Math.Min(MinimumStep, Math.Abs(delta));
        }

        _shown += step;
        Draw(_shown);
    }

    /// <summary>Positions the star and its corona, which XAML anchors at the top left.</summary>
    private void PlaceCentre()
    {
        if (this.FindControl<Panel>("Centre") is { } centre)
        {
            centre.Margin = new Thickness(
                CentreX - (CentreSize / 2),
                CentreY - (CentreSize / 2),
                0,
                0);
        }
    }

    /// <summary>
    /// Scatters the stars. Seeded, so the field is composed rather than merely
    /// random — the same sky greets the user every launch.
    /// </summary>
    /// <remarks>
    /// They cover a square wider than the window and centred on the field's
    /// rotation origin, because a field only as large as the frame would sweep
    /// its own empty corners into view as it turns.
    /// </remarks>
    private void BuildStarfield()
    {
        if (this.FindControl<Canvas>("Stars") is not { } canvas)
        {
            return;
        }

        var random = new Random(20260811);
        const double reach = 460;

        for (var i = 0; i < 150; i++)
        {
            var angle = random.NextDouble() * Math.Tau;

            // Square-rooted so the stars spread evenly over the area rather than
            // crowding the centre.
            var distance = Math.Sqrt(random.NextDouble()) * reach;

            var size = 0.7 + Math.Pow(random.NextDouble(), 2.4) * 2.1;

            var x = CentreX + (Math.Cos(angle) * distance);
            var y = CentreY + (Math.Sin(angle) * distance);

            // The wordmark's band is kept clear. With a fixed seed a star lands in
            // the same place every launch, and one of them sat just off the end of
            // "APHELION", where it read as a full stop.
            if (Math.Abs(x - CentreX) < 170 && y > 306 && y < 384)
            {
                continue;
            }

            var star = new Ellipse
            {
                Width = size,
                Height = size,
                Opacity = 0.16 + random.NextDouble() * 0.7,
            };

            star.Classes.Add("star");

            star.Classes.Add((i % 7) switch
            {
                0 or 1 => "twinkle-slow",
                2 or 3 => "twinkle-mid",
                4 => "twinkle-fast",

                // The rest hold steady. A sky where everything blinks reads as
                // noise rather than as depth.
                _ => "star",
            });

            Canvas.SetLeft(star, x - (size / 2));
            Canvas.SetTop(star, y - (size / 2));

            canvas.Children.Add(star);
        }
    }

    /// <summary>Creates the orbit's parts once; only their geometry changes afterwards.</summary>
    private void BuildOrbit()
    {
        if (this.FindControl<Canvas>("Orbit") is not { } canvas)
        {
            return;
        }

        // The path not yet travelled, barely there.
        _track.Stroke = new SolidColorBrush(Color.Parse("#2B2551"));
        _track.StrokeThickness = 1;
        _track.Data = EllipseGeometry();

        // Two strokes make the glow: a wide faint one under a narrow bright one.
        // It costs one extra path and reads as light without an effect pass.
        var trail = TrailBrush();

        _sweptGlow.Stroke = trail;
        _sweptGlow.StrokeThickness = 9;
        _sweptGlow.Opacity = 0.14;
        _sweptGlow.StrokeLineCap = PenLineCap.Round;

        _swept.Stroke = trail;
        _swept.StrokeThickness = 2;
        _swept.Opacity = 0.75;
        _swept.StrokeLineCap = PenLineCap.Round;

        // The stretch just behind the body burns hotter, so the trail has a head
        // rather than ending flatly wherever progress happens to be.
        _headGlow.Stroke = new SolidColorBrush(Color.Parse("#7BE9F5"));
        _headGlow.StrokeThickness = 11;
        _headGlow.Opacity = 0.2;
        _headGlow.StrokeLineCap = PenLineCap.Round;

        _head.Stroke = new SolidColorBrush(Color.Parse("#D8FBFF"));
        _head.StrokeThickness = 2.4;
        _head.Opacity = 0.95;
        _head.StrokeLineCap = PenLineCap.Round;

        // The destination, waiting. It brightens as the body closes on it.
        _aphelionRing.Width = 15;
        _aphelionRing.Height = 15;
        _aphelionRing.Stroke = new SolidColorBrush(Color.Parse("#8C83FF"));
        _aphelionRing.StrokeThickness = 1.2;
        _aphelionRing.Opacity = 0.25;
        Canvas.SetLeft(_aphelionRing, CentreX - RadiusX - 7.5);
        Canvas.SetTop(_aphelionRing, CentreY - 7.5);

        _bodyHalo.Width = 40;
        _bodyHalo.Height = 40;
        _bodyHalo.Fill = new RadialGradientBrush
        {
            GradientStops =
            [
                new GradientStop(Color.Parse("#B0AEF4FF"), 0),
                new GradientStop(Color.Parse("#404FD8E4"), 0.45),
                new GradientStop(Color.Parse("#004FD8E4"), 1),
            ],
        };
        _bodyHalo.Classes.Add("body-halo");

        _body.Width = 9;
        _body.Height = 9;
        _body.Fill = new RadialGradientBrush
        {
            GradientStops =
            [
                new GradientStop(Colors.White, 0),
                new GradientStop(Color.Parse("#BFF3FF"), 0.55),
                new GradientStop(Color.Parse("#5FD9E8"), 1),
            ],
        };

        canvas.Children.Add(_track);
        canvas.Children.Add(_aphelionRing);
        canvas.Children.Add(_sweptGlow);
        canvas.Children.Add(_swept);
        canvas.Children.Add(_headGlow);
        canvas.Children.Add(_head);
        canvas.Children.Add(_bodyHalo);
        canvas.Children.Add(_body);
    }

    /// <summary>Redraws the trail and moves the body for a progress of 0 to 1.</summary>
    private void Draw(double progress)
    {
        var clamped = Math.Clamp(progress, 0, 1);

        // Nothing is drawn at the very start: a trail before the body has moved
        // would claim progress that has not happened.
        _swept.Data = ArcTo(0, clamped);
        _sweptGlow.Data = _swept.Data;

        var headStart = Math.Max(0, clamped - HeadFraction);
        _head.Data = ArcTo(headStart, clamped);
        _headGlow.Data = _head.Data;

        var position = PointAt(clamped);

        Canvas.SetLeft(_body, position.X - (_body.Width / 2));
        Canvas.SetTop(_body, position.Y - (_body.Height / 2));
        Canvas.SetLeft(_bodyHalo, position.X - (_bodyHalo.Width / 2));
        Canvas.SetTop(_bodyHalo, position.Y - (_bodyHalo.Height / 2));

        // The destination lights up over the final stretch of the journey.
        _aphelionRing.Opacity = 0.25 + (Math.Pow(clamped, 3) * 0.75);
    }

    /// <summary>
    /// Where the body sits at a given progress. Progress runs from the near side
    /// of the orbit, through the top, to the aphelion at the far side — so
    /// finishing and arriving are the same moment.
    /// </summary>
    private static Point PointAt(double progress)
    {
        var angle = progress * Math.PI;

        // Y is negated because the screen's runs downwards, and the body should
        // climb over the top of the orbit rather than dip under it.
        return new Point(
            CentreX + (Math.Cos(angle) * RadiusX),
            CentreY - (Math.Sin(angle) * RadiusY));
    }

    /// <summary>
    /// The stretch of orbit between two progress values, as a polyline. Built from
    /// points rather than an arc segment because an ellipse arc has to be told its
    /// sweep and largeness, and getting either wrong fails silently by drawing the
    /// long way round.
    /// </summary>
    private static PathGeometry? ArcTo(double from, double to)
    {
        var span = to - from;

        if (span <= 0.0008)
        {
            return null;
        }

        var steps = Math.Max(2, (int)Math.Ceiling(ArcResolution * span));
        var points = new Points();

        for (var i = 0; i <= steps; i++)
        {
            points.Add(PointAt(from + (span * i / steps)));
        }

        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = false,
            IsFilled = false,
            Segments = [new PolyLineSegment(points.Skip(1))],
        };

        return new PathGeometry { Figures = [figure] };
    }

    /// <summary>The full orbit, drawn the same way so the track and trail align exactly.</summary>
    private static PathGeometry EllipseGeometry()
    {
        var points = new Points();

        for (var i = 0; i <= ArcResolution * 2; i++)
        {
            var angle = Math.Tau * i / (ArcResolution * 2);
            points.Add(new Point(
                CentreX + (Math.Cos(angle) * RadiusX),
                CentreY - (Math.Sin(angle) * RadiusY)));
        }

        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = true,
            IsFilled = false,
            Segments = [new PolyLineSegment(points.Skip(1))],
        };

        return new PathGeometry { Figures = [figure] };
    }

    /// <summary>
    /// Violet where the journey began, cyan at the far end: the logo's own
    /// gradient, laid along the orbit.
    /// </summary>
    private static LinearGradientBrush TrailBrush() =>
        new LinearGradientBrush
        {
            StartPoint = new RelativePoint(
                CentreX + RadiusX,
                CentreY,
                RelativeUnit.Absolute),
            EndPoint = new RelativePoint(
                CentreX - RadiusX,
                CentreY,
                RelativeUnit.Absolute),
            GradientStops =
            [
                new GradientStop(Color.Parse("#8C83FF"), 0),
                new GradientStop(Color.Parse("#6E9BFF"), 0.45),
                new GradientStop(Color.Parse("#3FD8E4"), 1),
            ],
        };
}
