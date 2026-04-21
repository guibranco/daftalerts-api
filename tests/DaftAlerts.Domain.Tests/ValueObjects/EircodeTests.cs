using DaftAlerts.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace DaftAlerts.Domain.Tests.ValueObjects;

public sealed class EircodeTests
{
    [Theory]
    [InlineData("D02KC86", "D02KC86", "D02")]
    [InlineData("D02 KC86", "D02KC86", "D02")]
    [InlineData("d02kc86", "D02KC86", "D02")]
    [InlineData("  A96W5P0  ", "A96W5P0", "A96")]
    [InlineData("T12 X5P0", "T12X5P0", "T12")]
    public void Parse_should_normalize_and_uppercase(string input, string expectedValue, string expectedRk)
    {
        Eircode.TryParse(input, out var e).Should().BeTrue();
        e.Value.Should().Be(expectedValue);
        e.RoutingKey.Should().Be(expectedRk);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("B02KC86")] // B is not a valid Eircode first letter
    [InlineData("D2KC86")] // missing digit
    [InlineData("D022KC86")] // extra digit
    public void Parse_should_reject_invalid(string? input)
    {
        Eircode.TryParse(input, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("Herbert Lane Mews, Dublin 2, D02KC86", "D02KC86")]
    [InlineData("The address is D02 KC86 in the middle", "D02KC86")]
    [InlineData("No eircode here at all.", null)]
    public void Extract_should_find_first_match(string text, string? expected)
    {
        var result = Eircode.Extract(text);
        if (expected is null)
            result.Should().BeNull();
        else
            result!.Value.Value.Should().Be(expected);
    }
}
