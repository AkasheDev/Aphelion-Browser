using Avalonia.Data.Converters;

namespace Aphelion.Desktop.UI.ViewModels;

public static class DoubleConverters
{
    /// <summary>
    /// Width of the new-tab button plus its margins, as styled: 28px button and
    /// 4px either side.
    /// </summary>
    private const double NewTabButtonWidth = 36;

    /// <summary>
    /// Leaves room for the new-tab button beside the tab strip.
    /// </summary>
    /// <remarks>
    /// The button sits after the strip, so a strip allowed the full width pushes
    /// it off the end and under the caption buttons once the tabs fill the bar.
    /// </remarks>
    public static readonly IValueConverter LessNewTabButton =
        new FuncValueConverter<double, double>(width =>
            // Before the first layout pass the width is zero. Capping the strip at
            // zero there would hide every tab and never recover, so leave it
            // unbounded until a real measurement arrives.
            width <= 0 ? double.PositiveInfinity : Math.Max(0, width - NewTabButtonWidth));
}
