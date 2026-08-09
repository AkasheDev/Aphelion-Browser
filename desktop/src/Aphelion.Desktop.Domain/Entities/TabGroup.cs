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
    public TabGroup(TabGroupId id, string name, GroupColor color)
    {
        Id = id;
        Color = color;
        Rename(name);
    }

    public TabGroupId Id { get; }

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
