using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Threading;
using Aphelion.Desktop.UI.ViewModels;

namespace Aphelion.Desktop.UI.Controls;

/// <summary>
/// Lays tabs out left to right, shrinking them evenly as more open so the strip
/// never scrolls, in the manner of Chrome.
/// </summary>
/// <remarks>
/// Tabs share the available width equally, capped at
/// <see cref="TabStripMetrics.MaxTabWidth"/> and floored at
/// <see cref="TabStripMetrics.MinTabWidth"/>. The floor leaves room for the
/// favicon, one character of the title and the close button; past that point tabs
/// stop shrinking and the overflow is simply clipped, which is the same trade
/// Chrome makes. Group chips keep their natural width — they are labels, not tabs,
/// and squeezing them would make a group unreadable long before the tabs suffer.
/// <para>
/// The panel reports the room it is offered and nothing more. Deciding how many
/// tabs fit belongs to the shell: see <see cref="TabStripMetrics"/> for why an
/// answer measured from the panel's own children cannot be stable.
/// </para>
/// <para>
/// Widths are eased rather than jumped. A tab already fades and scales itself in
/// and out, but every other tab used to snap to its new share the instant one
/// opened or closed - so the tab being opened was the only smooth thing on a
/// strip where everything else teleported. Each child is arranged along a curve
/// from where it was to where it now belongs.
/// </para>
/// </remarks>
public sealed class TabStripPanel : Panel
{
    /// <summary>Matches the fade the tab itself runs on the way in and out.</summary>
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(160);

    private static readonly CubicEaseOut Ease = new();

    /// <summary>The last width reported, so an unchanged layout stays silent.</summary>
    private double _reportedRoom = -1;

    /// <summary>Where each child was last told it belongs.</summary>
    private readonly Dictionary<Control, Rect> _settled = [];

    /// <summary>Children still travelling, and what they are travelling between.</summary>
    private readonly Dictionary<Control, Motion> _moving = [];

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private DispatcherTimer? _frames;

    /// <summary>The room the last measure was offered, and the room the last
    /// arrange was taken against.</summary>
    private double _room = -1;
    private double _arrangedRoom = -1;
    private bool _arrangedOnce;

    private readonly record struct Motion(Rect From, Rect To, TimeSpan Start);

    protected override Size MeasureOverride(Size availableSize)
    {
        // The room, taken before any child is measured: it is what the window
        // offers, so it cannot depend on what is currently in the strip.
        ReportRoom(availableSize.Width);

        if (!double.IsInfinity(availableSize.Width))
        {
            _room = availableSize.Width;
        }

        // Chips are measured at their natural width first; whatever is left is
        // what the tabs have to share.
        var chrome = 0d;
        var weight = 0d;

        foreach (var child in Children)
        {
            if (IsTab(child))
            {
                weight += WeightOf(child);
            }
            else
            {
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                chrome += child.DesiredSize.Width;
            }
        }

        var unit = UnitFor(weight, availableSize.Width - chrome);
        var total = chrome;
        var height = 0d;
        var now = _clock.Elapsed;

        foreach (var child in Children)
        {
            if (IsTab(child))
            {
                // Measured at the width it is heading for, not the width it is
                // passing through, so nothing inside reflows on every frame.
                var width = WidthFor(child, unit);
                child.Measure(new Size(width, availableSize.Height));

                // Reported at the width it is passing through, though. The new
                // tab button sits outside this panel and follows the size given
                // back from here, so reporting the destination made it jump to
                // the end of the strip while the tab was still growing into it.
                total += WidthNow(child, width, now);
            }

            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(
            double.IsInfinity(availableSize.Width) ? total : Math.Min(total, availableSize.Width),
            height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var targets = TargetsFor(finalSize);
        Forget(targets);

        var now = _clock.Elapsed;

        if (ShouldSnap(targets))
        {
            foreach (var (child, target) in targets)
            {
                _settled[child] = target;
                child.Arrange(target);
            }

            _moving.Clear();
            StopFrames();
        }
        else
        {
            foreach (var (child, target) in targets)
            {
                StartMotion(child, target, now);
            }

            var travelling = false;

            foreach (var (child, target) in targets)
            {
                child.Arrange(RectAt(child, target, now, out var arrived));
                travelling |= !arrived;
            }

            if (travelling)
            {
                RunFrames();
            }
            else
            {
                _moving.Clear();
                StopFrames();
            }
        }

        _arrangedOnce = true;
        _arrangedRoom = _room;
        return finalSize;
    }

    /// <summary>Where every child belongs once the strip has settled.</summary>
    private List<(Control Child, Rect Target)> TargetsFor(Size finalSize)
    {
        var chrome = 0d;
        var weight = 0d;

        foreach (var child in Children)
        {
            if (IsTab(child))
            {
                weight += WeightOf(child);
            }
            else
            {
                chrome += child.DesiredSize.Width;
            }
        }

        var unit = UnitFor(weight, finalSize.Width - chrome);
        var x = 0d;
        var targets = new List<(Control, Rect)>(Children.Count);

        foreach (var child in Children)
        {
            var width = IsTab(child) ? WidthFor(child, unit) : child.DesiredSize.Width;
            targets.Add((child, new Rect(x, 0, width, finalSize.Height)));
            x += width;
        }

        return targets;
    }

    /// <summary>
    /// Whether this layout should be taken in one step.
    /// </summary>
    /// <remarks>
    /// Easing is for the strip reflowing around a tab that opened or closed,
    /// which is the case where every width changes at once. A reorder leaves
    /// every width alone and only moves tabs past each other, and that already
    /// has an owner: the drag handler slides the containers with a render
    /// transform. Animating the layout underneath it as well would move each tab
    /// twice. The first layout and a window resize are snapped for the same
    /// reason from the other direction - there is nothing there to ease from,
    /// and a strip that lags the window edge feels broken rather than smooth.
    /// </remarks>
    private bool ShouldSnap(List<(Control Child, Rect Target)> targets)
    {
        if (!_arrangedOnce || Math.Abs(_room - _arrangedRoom) > 0.5)
        {
            return true;
        }

        foreach (var (child, target) in targets)
        {
            if (!_settled.TryGetValue(child, out var settled) ||
                Math.Abs(settled.Width - target.Width) > 0.5)
            {
                return false;
            }
        }

        return true;
    }

    private void StartMotion(Control child, Rect target, TimeSpan now)
    {
        if (_settled.TryGetValue(child, out var settled))
        {
            if (settled != target)
            {
                _moving[child] = new Motion(RectAt(child, settled, now, out _), target, now);
            }
        }
        else
        {
            // A tab that has just opened grows out of the gap it is taking
            // rather than appearing at full width and shoving its neighbours.
            _moving[child] = new Motion(target.WithWidth(0), target, now);
        }

        _settled[child] = target;
    }

    private Rect RectAt(Control child, Rect target, TimeSpan now, out bool arrived)
    {
        if (!_moving.TryGetValue(child, out var motion))
        {
            arrived = true;
            return target;
        }

        var elapsed = now - motion.Start;

        if (elapsed >= SlideDuration)
        {
            arrived = true;
            return motion.To;
        }

        arrived = false;
        var progress = Ease.Ease(elapsed / SlideDuration);

        return new Rect(
            Lerp(motion.From.X, motion.To.X, progress),
            Lerp(motion.From.Y, motion.To.Y, progress),
            Lerp(motion.From.Width, motion.To.Width, progress),
            Lerp(motion.From.Height, motion.To.Height, progress));
    }

    /// <summary>The width a child is showing right now, mid-travel or settled.</summary>
    private double WidthNow(Control child, double target, TimeSpan now)
    {
        if (!_moving.TryGetValue(child, out var motion))
        {
            return target;
        }

        var elapsed = now - motion.Start;

        return elapsed >= SlideDuration
            ? motion.To.Width
            : Lerp(motion.From.Width, motion.To.Width, Ease.Ease(elapsed / SlideDuration));
    }

    private static double Lerp(double from, double to, double progress) =>
        from + ((to - from) * progress);

    /// <summary>Drops children that have left the strip.</summary>
    private void Forget(List<(Control Child, Rect Target)> targets)
    {
        if (_settled.Count == targets.Count && _moving.Count == 0)
        {
            return;
        }

        var present = new HashSet<Control>(targets.Count);

        foreach (var (child, _) in targets)
        {
            present.Add(child);
        }

        foreach (var child in _settled.Keys.Where(child => !present.Contains(child)).ToList())
        {
            _settled.Remove(child);
            _moving.Remove(child);
        }
    }

    private void RunFrames()
    {
        _frames ??= CreateFrames();
        _frames.Start();
    }

    private void StopFrames() => _frames?.Stop();

    private DispatcherTimer CreateFrames()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };

        // Measure as well as arrange: the size this panel reports is what moves
        // the new tab button along with the end of the strip.
        timer.Tick += (_, _) => InvalidateMeasure();
        return timer;
    }

    /// <summary>
    /// Tells the shell how much room the strip has, so it can list whatever does
    /// not fit in the overflow panel rather than opening tabs off-screen.
    /// </summary>
    private void ReportRoom(double room)
    {
        // An unconstrained measure says nothing about the window.
        if (double.IsInfinity(room) || room <= 0 || Math.Abs(room - _reportedRoom) < 1)
        {
            return;
        }

        _reportedRoom = room;

        // Read from the panel's own DataContext, which is the shell. An earlier
        // version routed this through a static hook the window installed, but the
        // first layout passes run before the window is opened, so the hook was
        // still null when it mattered.
        //
        // Posted rather than called straight out: acting on it rebuilds the
        // strip's items, and changing a collection in the middle of the layout
        // pass reading it does not take effect.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (DataContext is ShellViewModel shell)
                {
                    shell.ReportStripRoom(room);
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Width of one unit of weight: what is left over, divided by the total
    /// weight on the strip.
    /// </summary>
    private static double UnitFor(double totalWeight, double available)
    {
        if (totalWeight <= 0)
        {
            return 0;
        }

        return double.IsInfinity(available) || available <= 0
            ? TabStripMetrics.MaxTabWidth
            : available / totalWeight;
    }

    /// <summary>
    /// A tab's share, clamped between its own floor and the comfortable maximum.
    /// A split pair claims more, because it holds two of everything.
    /// </summary>
    private static double WidthFor(Control child, double unit)
    {
        var split = IsSplit(child);

        var floor = split ? TabStripMetrics.MinSplitTabWidth : TabStripMetrics.MinTabWidth;
        var ceiling = split
            ? TabStripMetrics.MaxTabWidth * TabStripMetrics.SplitWeight
            : TabStripMetrics.MaxTabWidth;

        return Math.Clamp(unit * WeightOf(child), floor, ceiling);
    }

    private static double WeightOf(Control child) =>
        IsSplit(child) ? TabStripMetrics.SplitWeight : 1d;

    private static bool IsTab(Control child) => child.DataContext is TabItemViewModel;

    private static bool IsSplit(Control child) =>
        child.DataContext is TabItemViewModel { IsSplit: true };
}
