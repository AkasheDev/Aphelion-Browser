using Avalonia;
using Avalonia.Controls;
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
    /// <summary>
    /// Called with how many tabs the strip can hold at its current width, so the
    /// shell can list the remainder in the overflow panel.
    /// </summary>
    /// <remarks>
    /// A static handler rather than an event on the instance: the panel is built
    /// by the ItemsControl's template and does not exist when the window wires
    /// itself up, so there is nothing to subscribe to at that point.
    /// </remarks>
    public static Action<int>? CapacityReporter { get; set; }

    private int _reportedCapacity = -1;
    /// <summary>Widest a tab is allowed to be, matching Chrome's comfortable size.</summary>
    public const double MaxTabWidth = 240;

    /// <summary>
    /// Narrowest a tab may become, sized so the favicon, the first character of
    /// the title and the close button all survive.
    /// </summary>
    /// <remarks>
    /// Measured from the styles rather than estimated: margin 4 + padding 14 +
    /// favicon 15 with its 9px gap + one character at 12px Inter, allowing 10px +
    /// close button 20 with its 6px gap = 78. A first attempt at 74 lost the
    /// character entirely, which a screenshot at twenty tabs made obvious.
    /// </remarks>
    public const double MinTabWidth = 78;

    protected override Size MeasureOverride(Size availableSize)
    {
        var chrome = 0d;
        var tabCount = 0;

        // Chips are measured at their natural width first; whatever is left is
        // what the tabs have to share.
        foreach (var child in Children)
        {
            if (IsTab(child))
            {
                tabCount++;
            }
            else
            {
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                chrome += child.DesiredSize.Width;
            }
        }

        var tabWidth = TabWidthFor(tabCount, availableSize.Width - chrome);
        var height = 0d;

        foreach (var child in Children)
        {
            if (IsTab(child))
            {
                child.Measure(new Size(tabWidth, availableSize.Height));
            }

            height = Math.Max(height, child.DesiredSize.Height);
        }

        var total = chrome + (tabWidth * tabCount);

        return new Size(
            double.IsInfinity(availableSize.Width) ? total : Math.Min(total, availableSize.Width),
            height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var chrome = 0d;
        var tabCount = 0;

        foreach (var child in Children)
        {
            if (IsTab(child))
            {
                tabCount++;
            }
            else
            {
                chrome += child.DesiredSize.Width;
            }
        }

        var room = finalSize.Width - chrome;
        var tabWidth = TabWidthFor(tabCount, room);

        // How many tabs would still fit at the floor. Reported so the shell can
        // list the rest in the overflow panel rather than opening tabs off-screen.
        var capacity = Math.Max(1, (int)Math.Floor(room / MinTabWidth));

        if (capacity != _reportedCapacity)
        {
            _reportedCapacity = capacity;
            CapacityReporter?.Invoke(capacity);
        }

        var x = 0d;

        foreach (var child in Children)
        {
            var width = IsTab(child) ? tabWidth : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }

    /// <summary>
    /// The width each tab gets: an equal share of what is left, clamped between
    /// the comfortable maximum and the floor.
    /// </summary>
    private static double TabWidthFor(int tabCount, double available)
    {
        if (tabCount == 0)
        {
            return 0;
        }

        if (double.IsInfinity(available) || available <= 0)
        {
            return MaxTabWidth;
        }

        return Math.Clamp(available / tabCount, MinTabWidth, MaxTabWidth);
    }

    private static bool IsTab(Control child) => child.DataContext is TabItemViewModel;
}
