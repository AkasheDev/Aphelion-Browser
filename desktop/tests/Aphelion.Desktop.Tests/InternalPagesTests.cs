using Aphelion.Desktop.Domain;
using Xunit;

namespace Aphelion.Desktop.Tests;

public sealed class InternalPagesTests
{
    [Theory]
    [InlineData("aphelion://downloads", InternalPageKind.Downloads)]
    [InlineData("APHELION://Settings/", InternalPageKind.Settings)]
    [InlineData("aphelion://history", InternalPageKind.History)]
    public void Matches_known_hosts(string input, InternalPageKind expected)
    {
        Assert.True(InternalPages.TryMatch(input, out var kind));
        Assert.Equal(expected, kind);
        Assert.Equal(kind, expected);
    }

    [Fact]
    public void Unknown_host_is_still_an_aphelion_address()
    {
        Assert.True(InternalPages.IsAphelionAddress("aphelion://flags"));
        Assert.False(InternalPages.TryMatch("aphelion://flags", out _));
    }
}
