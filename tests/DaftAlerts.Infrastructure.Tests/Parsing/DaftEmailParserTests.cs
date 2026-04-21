using System;
using System.Threading.Tasks;
using DaftAlerts.Application.Parsing;
using DaftAlerts.Infrastructure.Parsing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DaftAlerts.Infrastructure.Tests.Parsing;

public sealed class DaftEmailParserTests
{
    private static IDaftEmailParser CreateParser() => new DaftEmailParser(NullLogger<DaftEmailParser>.Instance);

    [Fact]
    public async Task Parses_Herbert_Lane_sample_completely()
    {
        var msg = await MimeHelper.LoadAsync("sample-daft-herbert-lane.eml");
        var parsed = CreateParser().Parse(msg.HtmlBody ?? "", msg.Subject, msg.Date.UtcDateTime, msg.MessageId);

        parsed.Should().NotBeNull();
        parsed!.DaftId.Should().Be("6546017");
        parsed.DaftUrl.Should().Be("https://www.daft.ie/for-rent/house-herbert-lane-mews-dublin-2/6546017");
        parsed.Address.Should().Contain("Herbert Lane Mews").And.Contain("Dublin 2").And.Contain("D02KC86");
        parsed.Eircode.Should().Be("D02KC86");
        parsed.RoutingKey.Should().Be("D02");
        parsed.PriceMonthly.Should().Be(2850m);
        parsed.Beds.Should().Be(2);
        parsed.Baths.Should().Be(2);
        parsed.PropertyType.Should().Be("House");
        parsed.BerRating.Should().Be("C1");
        parsed.MainImageUrl.Should().Contain("media.daft.ie");
    }

    [Fact]
    public async Task Parses_apartment()
    {
        var msg = await MimeHelper.LoadAsync("sample-apartment.eml");
        var parsed = CreateParser().Parse(msg.HtmlBody ?? "", msg.Subject, msg.Date.UtcDateTime, msg.MessageId);

        parsed.Should().NotBeNull();
        parsed!.DaftId.Should().Be("7891234");
        parsed.PropertyType.Should().Be("Apartment");
        parsed.PriceMonthly.Should().Be(1950m);
        parsed.Beds.Should().Be(1);
        parsed.Baths.Should().Be(1);
        parsed.Eircode.Should().Be("D01XY12");
        parsed.RoutingKey.Should().Be("D01");
        parsed.BerRating.Should().Be("A3");
    }

    [Fact]
    public async Task Parses_studio()
    {
        var msg = await MimeHelper.LoadAsync("sample-studio.eml");
        var parsed = CreateParser().Parse(msg.HtmlBody ?? "", msg.Subject, msg.Date.UtcDateTime, msg.MessageId);

        parsed.Should().NotBeNull();
        parsed!.PropertyType.Should().Be("Studio");
        parsed.PriceMonthly.Should().Be(1450m);
        parsed.Beds.Should().Be(0);
        parsed.Baths.Should().Be(1);
        parsed.Eircode.Should().Be("D04PP88");
        parsed.BerRating.Should().Be("B2");
    }

    [Fact]
    public async Task Parses_shared_with_no_BER()
    {
        var msg = await MimeHelper.LoadAsync("sample-shared-no-ber.eml");
        var parsed = CreateParser().Parse(msg.HtmlBody ?? "", msg.Subject, msg.Date.UtcDateTime, msg.MessageId);

        parsed.Should().NotBeNull();
        parsed!.PropertyType.Should().Be("Shared");
        parsed.PriceMonthly.Should().Be(850m);
        parsed.BerRating.Should().BeNull("no BER rating image was present in the email");
        parsed.Eircode.Should().Be("D08AB12");
    }

    [Fact]
    public async Task Unwraps_Outlook_SafeLinks_and_prefers_originalsrc()
    {
        var msg = await MimeHelper.LoadAsync("sample-outlook-safelinks.eml");
        var parsed = CreateParser().Parse(msg.HtmlBody ?? "", msg.Subject, msg.Date.UtcDateTime, msg.MessageId);

        parsed.Should().NotBeNull();
        parsed!.DaftId.Should().Be("4442222");
        parsed.DaftUrl.Should().Contain("daft.ie/for-rent/apartment-rathmines-dublin-6/4442222");
        parsed.DaftUrl.Should().NotContain("safelinks.protection.outlook.com");
        parsed.Eircode.Should().Be("D06F6F6");
    }

    [Fact]
    public async Task Degrades_gracefully_when_eircode_is_missing()
    {
        var msg = await MimeHelper.LoadAsync("sample-no-eircode.eml");
        var parsed = CreateParser().Parse(msg.HtmlBody ?? "", msg.Subject, msg.Date.UtcDateTime, msg.MessageId);

        parsed.Should().NotBeNull();
        parsed!.DaftId.Should().Be("9998888");
        parsed.Eircode.Should().BeNull();
        parsed.RoutingKey.Should().BeNull();
        parsed.Address.Should().Contain("An Unnamed Street");
        parsed.BerRating.Should().Be("EXEMPT");
        parsed.PriceMonthly.Should().Be(3200m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>Nothing useful here</body></html>")]
    public void Returns_null_on_unparseable_html(string htmlBody)
    {
        var parsed = CreateParser().Parse(htmlBody, "subject", DateTime.UtcNow, null);
        parsed.Should().BeNull();
    }

    [Fact]
    public void Returns_null_when_price_missing_but_url_present()
    {
        const string html =
            "<html><body><a href='https://www.daft.ie/for-rent/foo/123'>Link</a></body></html>";
        var parsed = CreateParser().Parse(html, "Subject without price", DateTime.UtcNow, null);
        parsed.Should().BeNull();
    }

    [Theory]
    [InlineData("https://eur01.safelinks.protection.outlook.com/?url=https%3A%2F%2Fwww.daft.ie%2Ffoo&amp;data=x",
                "https://www.daft.ie/foo")]
    [InlineData("https://www.daft.ie/already-unwrapped",
                "https://www.daft.ie/already-unwrapped")]
    [InlineData("not-a-url-at-all",
                "not-a-url-at-all")]
    public void UnwrapSafeLink_handles_various_inputs(string input, string expected)
    {
        DaftEmailParser.UnwrapSafeLink(input).Should().Be(expected);
    }
}
