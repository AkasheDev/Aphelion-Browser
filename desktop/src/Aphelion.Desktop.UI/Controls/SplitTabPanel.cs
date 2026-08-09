using Avalonia;
using Avalonia.Controls;

namespace Aphelion.Desktop.UI.Controls;

/// <summary>
/// Lays out the contents of a tab: the leading half, an optional divider, and an
/// optional trailing half for a split pair.
/// </summary>
/// <remarks>
/// A Grid cannot do this. Auto columns offer their children unlimited width, so a
/// long title measures at full length and spills over its neighbour; star columns
/// keep their share even when collapsed, leaving a gap on ordinary tabs. This
/// panel measures the fixed parts first and divides only what is genuinely left,
/// so neither half can push past the tab.
/// </remarks>
public sealed class SplitTabPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var visible = new List<Control>();

        foreach (var child in Children)
        {
            if (child.IsVisible)
            {
                visible.Add(child);
            }
        }

        // The divider, and anything that is not a half, takes its natural size.
        var fixedWidth = 0d;
        var halves = new List<Control>();

        foreach (var child in visible)
        {
            if (IsHalf(child))
            {
                halves.Add(child);
            }
            else
            {
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                fixedWidth += child.DesiredSize.Width;
            }
        }

        var share = halves.Count == 0
            ? 0
            : Math.Max(0, width - fixedWidth) / halves.Count;

        var height = 0d;

        foreach (var child in visible)
        {
            if (IsHalf(child))
            {
                child.Measure(new Size(share, availableSize.Height));
            }

            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var fixedWidth = 0d;
        var halfCount = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            if (IsHalf(child))
            {
                halfCount++;
            }
            else
            {
                fixedWidth += child.DesiredSize.Width;
            }
        }

        var share = halfCount == 0
            ? 0
            : Math.Max(0, finalSize.Width - fixedWidth) / halfCount;

        var x = 0d;

        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                child.Arrange(default);
                continue;
            }

            var childWidth = IsHalf(child) ? share : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, childWidth, finalSize.Height));
            x += childWidth;
        }

        return finalSize;
    }

    /// <summary>
    /// A "half" is one side of the tab — icon plus title — and shares the space
    /// equally with the other. Marked with the <c>half</c> style class.
    /// </summary>
    private static bool IsHalf(Control child) => child.Classes.Contains("half");
}
