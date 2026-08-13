namespace Aphelion.Desktop.Domain.ValueObjects;

/// <summary>
/// Identifies a bookmark or a bookmark folder.
/// </summary>
/// <remarks>
/// One id type covers both kinds deliberately. A folder is a legitimate parent
/// for a bookmark and both are draggable, so the code that moves nodes around
/// treats them uniformly; separate id types would force a cast at every one of
/// those points for no gain in safety.
/// </remarks>
public readonly record struct BookmarkNodeId(Guid Value)
{
    public static BookmarkNodeId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}
