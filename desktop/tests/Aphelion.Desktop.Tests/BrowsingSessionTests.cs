using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.ValueObjects;
using Xunit;

namespace Aphelion.Desktop.Tests;

public sealed class BrowsingSessionTests
{
    [Fact]
    public void Pin_moves_the_tab_to_the_left_cluster()
    {
        var session = new BrowsingSession();
        var first = session.OpenTab();
        var second = session.OpenTab();
        var third = session.OpenTab();

        Assert.True(session.PinTab(third.Id));
        Assert.True(third.IsPinned);
        Assert.Equal(third.Id, session.Tabs[0].Id);
        Assert.Equal(first.Id, session.Tabs[1].Id);
        Assert.Equal(second.Id, session.Tabs[2].Id);
    }

    [Fact]
    public void Split_hides_the_partner_from_visible_tabs()
    {
        var session = new BrowsingSession();
        var left = session.OpenTab();
        var right = session.OpenTab();

        Assert.True(session.Split(left.Id, right.Id));
        Assert.Single(session.VisibleTabs);
        Assert.Equal(left.Id, session.VisibleTabs[0].Id);
        Assert.True(right.IsSplitPartner);
    }

    [Fact]
    public void Group_stays_contiguous_when_moved()
    {
        var session = new BrowsingSession();
        var a = session.OpenTab();
        var b = session.OpenTab();
        var c = session.OpenTab();
        var group = session.CreateGroup("Work", GroupColor.Violet);
        session.AddToGroup(a.Id, group.Id);
        session.AddToGroup(b.Id, group.Id);

        Assert.True(session.MoveGroup(group.Id, 1));
        Assert.Equal(c.Id, session.Tabs[0].Id);
        Assert.Equal(a.Id, session.Tabs[1].Id);
        Assert.Equal(b.Id, session.Tabs[2].Id);
    }
}
