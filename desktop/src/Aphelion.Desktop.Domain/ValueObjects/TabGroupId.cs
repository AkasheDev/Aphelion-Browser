namespace Aphelion.Desktop.Domain.ValueObjects;

/// <summary>
/// Identifies a tab group.
/// </summary>
public readonly record struct TabGroupId(Guid Value)
{
    public static TabGroupId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}
