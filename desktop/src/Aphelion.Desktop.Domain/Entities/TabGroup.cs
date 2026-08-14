using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Domain.Entities;

/// <summary>
/// A named, coloured grouping of tabs.
/// </summary>
/// <remarks>
/// A group owns no tabs itself; membership is recorded on the tab. Keeping the
/// relationship in one direction means a tab can never be listed in two groups or
/// be missing from the group it claims to belong to.
/// </remarks>
public sealed class TabGroup
{
    public TabGroup(
        TabGroupId id,
        string name,
        GroupColor color,
        SavedTabGroupId? savedGroupId = null)
    {
        Id = id;
        Color = color;
        SavedGroupId = savedGroupId;
        Rename(name);
    }

    public TabGroupId Id { get; }

    /// <summary>
    /// Durable saved-group record represented by this live group, when linked.
    /// Multiple windows may have different <see cref="Id"/> values carrying the
    /// same saved identity.
    /// </summary>
    public SavedTabGroupId? SavedGroupId { get; private set; }

    /// <summary>
    /// Links a legacy live group to its durable record. The link may be assigned
    /// once during migration, but can never be redirected to a different saved
    /// group afterwards.
    /// </summary>
    public void LinkSavedGroup(SavedTabGroupId savedGroupId)
    {
        if (SavedGroupId is { } existing && existing != savedGroupId)
        {
            throw new InvalidOperationException("A tab group cannot change its saved-group identity.");
        }

        SavedGroupId = savedGroupId;
    }

    public string Name { get; private set; } = string.Empty;

    public GroupColor Color { get; private set; }

    /// <summary>Collapsed groups show as a single chip instead of their tabs.</summary>
    public bool IsCollapsed { get; private set; }

    public void Rename(string name) =>
        Name = string.IsNullOrWhiteSpace(name) ? "Group" : name.Trim();

    public void Recolor(GroupColor color) => Color = color;

    public void Collapse() => IsCollapsed = true;

    public void Expand() => IsCollapsed = false;

    public void ToggleCollapsed() => IsCollapsed = !IsCollapsed;
}
