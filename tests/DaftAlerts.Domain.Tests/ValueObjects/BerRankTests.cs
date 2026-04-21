using DaftAlerts.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace DaftAlerts.Domain.Tests.ValueObjects;

public sealed class BerRankTests
{
    [Theory]
    [InlineData("A1", 1)]
    [InlineData("A3", 3)]
    [InlineData("B1", 4)]
    [InlineData("C3", 9)]
    [InlineData("G", 15)]
    [InlineData("Exempt", 99)]
    [InlineData("exempt", 99)]
    [InlineData("a1", 1)]
    public void Rank_maps_known_values(string input, int expected)
    {
        BerRank.Rank(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("H1")]
    [InlineData("nonsense")]
    public void Rank_returns_UnknownRank_for_invalid(string? input)
    {
        BerRank.Rank(input).Should().Be(BerRank.UnknownRank);
    }

    [Fact]
    public void A1_ranks_lower_than_G_which_ranks_lower_than_Exempt()
    {
        BerRank.Rank("A1").Should().BeLessThan(BerRank.Rank("G"));
        BerRank.Rank("G").Should().BeLessThan(BerRank.Rank("Exempt"));
    }

    [Theory]
    [InlineData("A1", true)]
    [InlineData("Exempt", true)]
    [InlineData("Z9", false)]
    [InlineData(null, false)]
    public void IsKnown_returns_expected(string? input, bool expected)
    {
        BerRank.IsKnown(input).Should().Be(expected);
    }
}
