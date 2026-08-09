using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Domain.Entities;

/// <summary>
/// The set of open tabs, their groups, and which tab is active.
/// </summary>
/// <remarks>
/// All rules about opening, closing, reordering and grouping tabs live here so the
/// presentation layer never has to reason about them. The invariant this type
/// guarantees: a session is either empty or has exactly one active tab, and tab
/// order is the order the user sees.
/// </remarks>
public sealed class BrowsingSession
{
    private readonly List<BrowserTab> _tabs = [];
    private readonly Dictionary<TabGroupId, TabGroup> _groups = [];

    public IReadOnlyList<BrowserTab> Tabs => _tabs;

    public IReadOnlyCollection<TabGroup> Groups => _groups.Values;

    public BrowserTab? ActiveTab { get; private set; }

    public bool IsEmpty => _tabs.Count == 0;

    public BrowserTab OpenTab(PageAddress? address = null, bool activate = true)
    {
        var tab = new BrowserTab(TabId.New(), address);
        _tabs.Add(tab);

        if (activate || ActiveTab is null)
        {
            ActiveTab = tab;
        }

        return tab;
    }

    /// <summary>
    /// Opens a tab directly after <paramref name="sibling"/> and in the same group.
    /// Used for links opened from a page, which the user expects to appear next to
    /// their origin rather than at the end of the strip.
    /// </summary>
    public BrowserTab OpenTabNextTo(BrowserTab sibling, PageAddress? address = null, bool activate = true)
    {
        ArgumentNullException.ThrowIfNull(sibling);

        var tab = new BrowserTab(TabId.New(), address);

        if (sibling.GroupId is { } groupId)
        {
            tab.JoinGroup(groupId);
        }

        var index = _tabs.IndexOf(sibling);
        _tabs.Insert(index < 0 ? _tabs.Count : index + 1, tab);

        if (activate)
        {
            ActiveTab = tab;
        }

        return tab;
    }

    /// <summary>
    /// Closes a tab and picks the next active one. Returns false when the tab was
    /// not part of this session.
    /// </summary>
    public bool CloseTab(TabId id)
    {
        var index = _tabs.FindIndex(t => t.Id == id);

        if (index < 0)
        {
            return false;
        }

        var wasActive = ActiveTab?.Id == id;
        _tabs.RemoveAt(index);

        if (wasActive)
        {
            // Prefer the tab that slid into this position, otherwise the one before
            // it. This is what every browser does and what the user expects.
            ActiveTab = _tabs.Count == 0
                ? null
                : _tabs[Math.Min(index, _tabs.Count - 1)];
        }

        DiscardEmptyGroups();
        return true;
    }

    public bool Activate(TabId id)
    {
        var tab = _tabs.Find(t => t.Id == id);

        if (tab is null)
        {
            return false;
        }

        ActiveTab = tab;
        return true;
    }

    /// <summary>Moves a tab to a new index, clamped to the valid range.</summary>
    public bool MoveTab(TabId id, int targetIndex)
    {
        var current = _tabs.FindIndex(t => t.Id == id);

        if (current < 0)
        {
            return false;
        }

        var clamped = Math.Clamp(targetIndex, 0, _tabs.Count - 1);

        if (clamped == current)
        {
            return false;
        }

        var tab = _tabs[current];
        _tabs.RemoveAt(current);
        _tabs.Insert(clamped, tab);
        return true;
    }

    public TabGroup CreateGroup(string name, GroupColor color)
    {
        var group = new TabGroup(TabGroupId.New(), name, color);
        _groups.Add(group.Id, group);
        return group;
    }

    public TabGroup? FindGroup(TabGroupId id) =>
        _groups.TryGetValue(id, out var group) ? group : null;

    public IReadOnlyList<BrowserTab> TabsInGroup(TabGroupId id) =>
        _tabs.FindAll(t => t.GroupId == id);

    /// <summary>
    /// Adds a tab to a group. The tab is moved next to the group's existing members
    /// so a group is always a contiguous run in the strip — a group split across the
    /// strip cannot be drawn coherently.
    /// </summary>
    public bool AddToGroup(TabId tabId, TabGroupId groupId)
    {
        var tab = _tabs.Find(t => t.Id == tabId);

        if (tab is null || !_groups.ContainsKey(groupId))
        {
            return false;
        }

        tab.JoinGroup(groupId);

        var lastIndex = _tabs.FindLastIndex(t => t.GroupId == groupId && t.Id != tabId);

        if (lastIndex >= 0)
        {
            MoveTab(tabId, lastIndex + 1);
        }

        return true;
    }

    public bool RemoveFromGroup(TabId tabId)
    {
        var tab = _tabs.Find(t => t.Id == tabId);

        if (tab?.GroupId is null)
        {
            return false;
        }

        tab.LeaveGroup();
        DiscardEmptyGroups();
        return true;
    }

    /// <summary>Closes every tab in a group and discards the group.</summary>
    public bool CloseGroup(TabGroupId groupId)
    {
        if (!_groups.ContainsKey(groupId))
        {
            return false;
        }

        foreach (var tab in TabsInGroup(groupId))
        {
            CloseTab(tab.Id);
        }

        _groups.Remove(groupId);
        return true;
    }

    /// <summary>
    /// Drops groups that no longer hold any tabs. An empty group is invisible in the
    /// strip, so leaving it around would leak state the user cannot see or remove.
    /// </summary>
    private void DiscardEmptyGroups()
    {
        var empty = _groups.Keys.Where(id => !_tabs.Exists(t => t.GroupId == id)).ToList();

        foreach (var id in empty)
        {
            _groups.Remove(id);
        }
    }
}
