using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using DaftAlerts.Application.Parsing;
using DaftAlerts.Domain.ValueObjects;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace DaftAlerts.Infrastructure.Parsing;

/// <summary>
/// Parses Daft.ie property-alert emails.
/// See <c>docs/PARSER.md</c> for the structural assumptions; the parser is defensive —
/// any single field missing only strips that field, not the whole record (except <see cref="ParsedDaftEmail.DaftId"/>,
/// which is required for idempotency).
/// </summary>
public sealed partial class DaftEmailParser : IDaftEmailParser
{
    private readonly ILogger<DaftEmailParser> _logger;

    public DaftEmailParser(ILogger<DaftEmailParser> logger)
    {
        _logger = logger;
    }

    // --- Regexes ------------------------------------------------------------

    [GeneratedRegex(@"€\s*([\d,]+(?:\.\d{2})?)\s*per\s*month", RegexOptions.IgnoreCase, "en-IE")]
    private static partial Regex PriceRegex();

    [GeneratedRegex(@"(\d+)\s*Bed\b", RegexOptions.IgnoreCase)]
    private static partial Regex BedsRegex();

    [GeneratedRegex(@"(\d+)\s*Bath\b", RegexOptions.IgnoreCase)]
    private static partial Regex BathsRegex();

    [GeneratedRegex(@"/ber/([A-G]\d?|Exempt)\.png", RegexOptions.IgnoreCase)]
    private static partial Regex BerRegex();

    [GeneratedRegex(@"daft\.ie/for-rent/[^/?#]+/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DaftListingRegex();

    // --- Entry point --------------------------------------------------------

    public ParsedDaftEmail? Parse(string htmlBody, string? subject, DateTime receivedAt, string? messageId)
    {
        subject ??= string.Empty;

        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            _logger.LogWarning("Daft email parse failed: empty HTML body (subject={Subject})", subject);
            return null;
        }

        var doc = new HtmlDocument { OptionAutoCloseOnEnd = true, OptionFixNestedTags = true };
        doc.LoadHtml(htmlBody);

        // --- URL + DaftId (required) ---------------------------------------
        var (daftUrl, daftId) = ExtractListingUrlAndId(doc);
        if (string.IsNullOrWhiteSpace(daftUrl) || string.IsNullOrWhiteSpace(daftId))
        {
            _logger.LogWarning("Daft email parse failed: no listing URL found (subject={Subject})", subject);
            return null;
        }

        // --- Address + Eircode ---------------------------------------------
        var (address, eircode) = ExtractAddressAndEircode(doc);
        var routingKey = eircode?[..3];

        // --- Subject-derived fields ----------------------------------------
        var propertyType = ExtractPropertyType(subject, doc);
        var price = ExtractPrice(subject, htmlBody);

        // --- Body-derived fields -------------------------------------------
        var beds = ExtractFirstInt(doc.DocumentNode.InnerText, BedsRegex());
        var baths = ExtractFirstInt(doc.DocumentNode.InnerText, BathsRegex());

        var berRating = ExtractBer(doc);
        var mainImageUrl = ExtractMainImage(doc);

        if (price <= 0)
        {
            _logger.LogWarning("Daft email parse failed: no price (subject={Subject})", subject);
            return null;
        }

        return new ParsedDaftEmail(
            DaftId: daftId!,
            DaftUrl: daftUrl!,
            Address: address,
            Eircode: eircode,
            RoutingKey: routingKey,
            PriceMonthly: price,
            Beds: beds,
            Baths: baths,
            PropertyType: propertyType,
            BerRating: berRating,
            MainImageUrl: mainImageUrl,
            ReceivedAt: receivedAt.ToUniversalTime(),
            RawSubject: subject ?? string.Empty,
            MessageId: messageId);
    }

    // --- Extraction helpers -------------------------------------------------

    private static (string? DaftUrl, string? DaftId) ExtractListingUrlAndId(HtmlDocument doc)
    {
        // Walk all <a> tags; prefer 'originalsrc' over 'href' if present (Outlook SafeLinks preserves the real URL there).
        foreach (var a in doc.DocumentNode.SelectNodes("//a") ?? Enumerable.Empty<HtmlNode>())
        {
            var candidates = new[]
            {
                a.GetAttributeValue("originalsrc", null),
                a.GetAttributeValue("href", null)
            };

            foreach (var raw in candidates)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var unwrapped = UnwrapSafeLink(raw.Trim());
                var m = DaftListingRegex().Match(unwrapped);
                if (m.Success)
                {
                    // Ensure we return the full URL
                    var full = unwrapped.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? unwrapped
                        : "https://www." + unwrapped.TrimStart('/');
                    return (full, m.Groups[1].Value);
                }
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Outlook SafeLinks wrap URLs as
    /// <c>https://{tenant}.safelinks.protection.outlook.com/?url=ENCODED&amp;data=...</c>.
    /// If the input matches that pattern, returns the decoded <c>url</c> parameter; otherwise returns the input.
    /// </summary>
    internal static string UnwrapSafeLink(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        if (url.IndexOf("safelinks.protection.outlook.com", StringComparison.OrdinalIgnoreCase) < 0) return url;

        try
        {
            var uri = new Uri(url);
            var query = HttpUtility.ParseQueryString(uri.Query);
            var inner = query.Get("url");
            if (string.IsNullOrWhiteSpace(inner)) return url;
            return WebUtility.UrlDecode(inner);
        }
        catch (UriFormatException)
        {
            return url;
        }
    }

    private static (string Address, string? Eircode) ExtractAddressAndEircode(HtmlDocument doc)
    {
        // Primary: any element with an explicit css-class="address" attribute (the Daft template uses this).
        var addressNode =
            doc.DocumentNode.SelectSingleNode("//*[@css-class='address']")
            ?? doc.DocumentNode.SelectSingleNode("//*[contains(@class,'address')]");

        var raw = addressNode is null ? null : HtmlEntity.DeEntitize(addressNode.InnerText).Trim();

        if (string.IsNullOrWhiteSpace(raw))
        {
            // Fallback: scan the full visible text for an Eircode; use the line containing it as the address.
            var eircodeFromBody = Eircode.Extract(doc.DocumentNode.InnerText);
            if (eircodeFromBody is null) return (string.Empty, null);

            var line = FindLineContaining(doc.DocumentNode.InnerText, eircodeFromBody.Value.Value);
            return (line?.Trim() ?? string.Empty, eircodeFromBody.Value.Value);
        }

        var address = Regex.Replace(raw, @"\s+", " ").Trim();
        var eircode = Eircode.Extract(address);
        return (address, eircode?.Value);
    }

    private static string? FindLineContaining(string text, string needle)
    {
        foreach (var line in text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return Regex.Replace(line, @"\s+", " ").Trim();
        }
        return null;
    }

    private static decimal ExtractPrice(string? subject, string htmlBody)
    {
        foreach (var source in new[] { subject ?? string.Empty, htmlBody })
        {
            var m = PriceRegex().Match(source);
            if (!m.Success) continue;
            var digits = m.Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal);
            if (decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                return value;
        }
        return 0m;
    }

    private static string ExtractPropertyType(string? subject, HtmlDocument doc)
    {
        // Subject has the canonical segment: "... House To Let, ..." or "... Apartment To Let, ...".
        string[] knownTypes = ["House", "Apartment", "Studio", "Shared"];
        if (!string.IsNullOrEmpty(subject))
        {
            foreach (var t in knownTypes)
            {
                if (subject.Contains(t, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
        }

        // Fallback: scan body.
        var bodyText = doc.DocumentNode.InnerText;
        foreach (var t in knownTypes)
        {
            if (Regex.IsMatch(bodyText, $@"\b{t}\b", RegexOptions.IgnoreCase))
                return t;
        }
        return "Other";
    }

    private static int ExtractFirstInt(string text, Regex regex)
    {
        var m = regex.Match(text);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
    }

    private static string? ExtractBer(HtmlDocument doc)
    {
        foreach (var img in doc.DocumentNode.SelectNodes("//img") ?? Enumerable.Empty<HtmlNode>())
        {
            var src = img.GetAttributeValue("src", null);
            if (string.IsNullOrWhiteSpace(src)) continue;
            var m = BerRegex().Match(src);
            if (m.Success)
            {
                var val = m.Groups[1].Value;
                return string.Equals(val, "Exempt", StringComparison.OrdinalIgnoreCase) ? "Exempt" : val.ToUpperInvariant();
            }
        }
        return null;
    }

    private static string? ExtractMainImage(HtmlDocument doc)
    {
        foreach (var img in doc.DocumentNode.SelectNodes("//img") ?? Enumerable.Empty<HtmlNode>())
        {
            var src = img.GetAttributeValue("src", null);
            if (string.IsNullOrWhiteSpace(src)) continue;
            if (src.Contains("media.daft.ie", StringComparison.OrdinalIgnoreCase))
                return src;
        }
        return null;
    }
}
