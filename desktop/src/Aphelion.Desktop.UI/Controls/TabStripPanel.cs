using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Aphelion.Desktop.UI.ViewModels;

namespace Aphelion.Desktop.UI.Controls;

/// <summary>
/// Lays tabs out left to right, shrinking them evenly as more open so the strip
/// never scrolls, in the manner of Chrome.
/// </summary>
/// <remarks>
/// Tabs share the available width equally, capped at <see cref="MaxTabWidth"/> and
/// floored at <see cref="MinTabWidth"/>. The floor leaves room for the favicon,
/// one character of the title and the close button; past that point tabs stop
/// shrinking and the overflow is simply clipped, which is the same trade Chrome
/// makes. Group chips keep their natural width — they are labels, not tabs, and
/// squeezing them would make a group unreadable long before the tabs suffer.
/// </remarks>
public sealed class TabStripPanel : Panel
{
    private int _reportedCapacity = -1;
    /// <summary>Widest a tab is allowed to be, matching Chrome's comfortable size.</summary>
    public const double MaxTabWidth = 240;

    /// <summary>
    /// Narrowest an ordinary tab may become, sized so the favicon, the first
    /// characters of the title and the close button all survive.
    /// </summary>
    /// <remarks>
    /// Measured from the styles rather than estimated: margin 4 + padding 14 +
    /// favicon 15 with its 9px gap + roughly two characters at 12px Inter, 18px +
    /// close button 20 with its 6px gap = 86.
    /// </remarks>
    public const double MinTabWidth = 86;

    /// <summary>
    /// Narrowest a split pair may become. It carries a second favicon, a divider
    /// and a second title, so squeezing it to an ordinary tab's floor leaves both
    /// names as bare ellipses.
    /// </summary>
    private const double MinSplitTabWidth = MinTabWidth + 15 + 9 + 18 + 17;

    /// <summary>
    /// How much more of the strip a split pair claims than an ordinary tab. It
    /// holds two of everything, so it earns a larger share before either side is
    /// squeezed.
    /// </summary>
    private const double SplitWeight = 1.7;

    protected override Size MeasureOverride(Size availableSize)
    {
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

        foreach (var child in Children)
        {
            if (IsTab(child))
            {
                var width = WidthFor(child, unit);
                child.Measure(new Size(width, availableSize.Height));
                total += width;
            }

            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(
            double.IsInfinity(availableSize.Width) ? total : Math.Min(total, availableSize.Width),
            height);
    }

    protected override Size ArrangeOverride(Size finalSize)
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

        var room = finalSize.Width - chrome;
        var unit = UnitFor(weight, room);

        // How many tabs would still fit at the floor. Measured against the widest
        // floor on the strip, so a split pair — which cannot shrink as far — does
        // not push the last tab off the end. Reported so the shell lists the rest
        // in the overflow panel rather than opening tabs off-screen.
        var floor = Children.Any(IsSplit) ? MinSplitTabWidth : MinTabWidth;
        var capacity = Math.Max(1, (int)Math.Floor(room / floor));

        if (capacity != _reportedCapacity)
        {
            _reportedCapacity = capacity;

            // Reported through the panel's own DataContext, which is the shell.
            // An earlier version routed this through a static hook the window
            // installed, but the first layout passes run before the window is
            // opened, so the hook was still null when it mattered.
            //
            // Posted rather than called straight out: acting on it rebuilds the
            // strip's items, and changing a collection in the middle of the
            // layout pass reading it does not take effect.
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (DataContext is ShellViewModel shell)
                    {
                        shell.ReportStripCapacity(capacity);
                    }
                },
                DispatcherPriority.Background);
        }

        var x = 0d;

        foreach (var child in Children)
        {
            var width = IsTab(child) ? WidthFor(child, unit) : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
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
            ? MaxTabWidth
            : available / totalWeight;
    }

    /// <summary>
    /// A tab's share, clamped between its own floor and the comfortable maximum.
    /// A split pair claims more, because it holds two of everything.
    /// </summary>
    private static double WidthFor(Control child, double unit)
    {
        var split = IsSplit(child);
        var floor = split ? MinSplitTabWidth : MinTabWidth;
        var ceiling = split ? MaxTabWidth * SplitWeight : MaxTabWidth;

        return Math.Clamp(unit * WeightOf(child), floor, ceiling);
    }

    private static double WeightOf(Control child) => IsSplit(child) ? SplitWeight : 1d;

    private static bool IsTab(Control child) => child.DataContext is TabItemViewModel;

    private static bool IsSplit(Control child) =>
        child.DataContext is TabItemViewModel { IsSplit: true };
}
