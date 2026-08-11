namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// The widths the tab strip is laid out with, shared by the panel that draws it
/// and the shell that decides how much of it fits.
/// </summary>
/// <remarks>
/// They live here rather than on the panel because both sides need them and only
/// one may own them. An earlier arrangement let the panel decide how many tabs
/// fit by measuring the children it had already been given, which made the answer
/// depend on itself: a split pair raised the floor, which pushed the pair into the
/// overflow panel, which lowered the floor again, which brought it back. The strip
/// flickered for as long as a split was open. The panel now reports the room it
/// has — a fact about the window, not about its contents — and the shell chooses
/// what goes in it.
/// </remarks>
public static class TabStripMetrics
{
    /// <summary>
    /// Widest a tab is allowed to be.
    /// </summary>
    /// <remarks>
    /// Chrome's ceiling is 240, and a lone tab sitting at it is mostly empty space
    /// around the words "New tab" — which is exactly how a freshly opened window
    /// looks, since one tab always gets the full ceiling. 200 is about the width
    /// tabs settle at once five or six are open, which is the proportion that
    /// reads correctly.
    /// </remarks>
    public const double MaxTabWidth = 200;

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
    public const double MinSplitTabWidth = MinTabWidth + 15 + 9 + 18 + 17;

    /// <summary>
    /// How much more of the strip a split pair claims than an ordinary tab. It
    /// holds two of everything, so it earns a larger share before either side is
    /// squeezed.
    /// </summary>
    public const double SplitWeight = 1.7;

    /// <summary>
    /// What a group chip is assumed to cost when deciding how many tabs fit. The
    /// real width follows the group's name, which only the panel knows; an estimate
    /// is enough here, because being a little out only shifts where the overflow
    /// begins, and the panel still shrinks whatever it is given to fit.
    /// </summary>
    public const double ChipCost = 96;

    /// <summary>
    /// Estimates a chip from the text it actually contains. The panel owns the
    /// exact font measurement; this keeps overflow decisions close enough that a
    /// long group name cannot consume several tabs' unbudgeted space.
    /// </summary>
    public static double ChipCostFor(string? name)
    {
        var characters = string.IsNullOrEmpty(name) ? 5 : name.Length;
        return Math.Clamp(30 + characters * 7d, 64, 220);
    }
}
