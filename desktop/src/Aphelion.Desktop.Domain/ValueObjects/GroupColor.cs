namespace Aphelion.Desktop.Domain.ValueObjects;

/// <summary>
/// The colour palette available to tab groups.
/// </summary>
/// <remarks>
/// A closed set rather than a free colour value: groups must stay legible against
/// both light and dark chrome, and an arbitrary colour cannot guarantee that. The
/// presentation layer maps each member to concrete brushes per theme.
/// </remarks>
public enum GroupColor
{
    Violet,
    Cyan,
    Emerald,
    Amber,
    Rose,
    Slate,
}
