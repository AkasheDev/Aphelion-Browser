namespace Aphelion.Desktop.Domain.ValueObjects;

/// <summary>
/// Identifies one saved tab group across browser sessions and windows.
/// </summary>
/// <remarks>
/// Unlike <see cref="TabGroupId"/>, which identifies one live occurrence of a
/// group inside a single browsing session, this id belongs to the durable saved
/// group record that those occurrences can reopen and update.
/// </remarks>
public readonly record struct SavedTabGroupId(Guid Value)
{
    public static SavedTabGroupId New() => new(Guid.NewGuid());

    /// <summary>
    /// Parses the persistence representation while rejecting Guid.Empty, which
    /// is a sentinel rather than a usable durable identity.
    /// </summary>
    public static bool TryParse(string? value, out SavedTabGroupId id)
    {
        if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
        {
            id = new SavedTabGroupId(parsed);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value.ToString("N");
}
