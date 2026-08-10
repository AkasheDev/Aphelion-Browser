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
/// </remarks>
public sealed class TabStripPanel : Panel
{
    /// <summary>The last width reported, so an unchanged layout stays silent.</summary>
    private double _reportedRoom = -1;

    protected override Size MeasureOverride(Size availableSize)
    {
        // The room, taken before any child is measured: it is what the window
        // offers, so it cannot depend on what is currently in the strip.
        ReportRoom(availableSize.Width);

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

        var unit = UnitFor(weight, finalSize.Width - chrome);
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
