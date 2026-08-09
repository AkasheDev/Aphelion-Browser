namespace Aphelion.Desktop.Domain.ValueObjects;

/// <summary>
/// Identifies a tab. A dedicated type rather than a bare Guid so a tab id cannot
/// be passed where some other identifier is expected.
/// </summary>
public readonly record struct TabId(Guid Value)
{
    public static TabId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}
