using Aphelion.Desktop.Domain;
using Aphelion.Desktop.Domain.ValueObjects;
using Xunit;

namespace Aphelion.Desktop.Tests;

public sealed class AddressBarInputTests
{
    [Fact]
    public void Empty_input_is_empty()
    {
        Assert.Equal(AddressBarIntent.Empty, AddressBarInput.Resolve("  ", out var address, out var search));
        Assert.Null(address);
        Assert.Equal(string.Empty, search);
    }

    [Fact]
    public void Host_with_a_dot_navigates()
    {
        var intent = AddressBarInput.Resolve("example.com", out var address, out _);
        Assert.Equal(AddressBarIntent.Navigate, intent);
        Assert.Equal("example.com", address?.DisplayHost);
    }

    [Fact]
    public void Phrase_with_spaces_searches()
    {
        var intent = AddressBarInput.Resolve("how to cook rice", out var address, out var search);
        Assert.Equal(AddressBarIntent.Search, intent);
        Assert.Null(address);
        Assert.Equal("how to cook rice", search);
    }

    [Fact]
    public void Known_aphelion_host_is_not_a_web_search()
    {
        Assert.Equal(
            AddressBarIntent.Empty,
            AddressBarInput.Resolve("aphelion://settings", out _, out _));
        Assert.True(InternalPages.TryMatch("aphelion://settings", out var kind));
        Assert.Equal(InternalPageKind.Settings, kind);
    }
}
