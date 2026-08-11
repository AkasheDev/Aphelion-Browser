namespace Aphelion.Desktop.Domain.ValueObjects;

/// <summary>Identity of a user-managed launcher on the New Tab page.</summary>
public readonly record struct NewTabShortcutId(Guid Value)
{
    public static NewTabShortcutId New() => new(Guid.NewGuid());
}
