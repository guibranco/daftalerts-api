# Daft.ie email parser

Implemented in `DaftAlerts.Infrastructure/Parsing/DaftEmailParser.cs`. Turns an HTML alert email body into a `ParsedDaftEmail` DTO.

## Contract

```csharp
public interface IDaftEmailParser
{
    ParsedDaftEmail? Parse(string htmlBody, string subject, DateTime receivedAt, string? messageId);
}
```

Returns `null` if parsing fails to produce a viable result — specifically if there is no `DaftId` (required for idempotency) or no price. All other fields degrade gracefully to `null` or defaults when missing.

## What it extracts

| Field | How it's found |
|---|---|
| `DaftId` | Regex over every `<a>` tag's `originalsrc` and `href` (after SafeLinks unwrap): `daft\.ie/for-rent/[^/?#]+/(\d+)`. |
| `DaftUrl` | Same match, unwrapped from Outlook SafeLinks if present. |
| `Address` | First element with `css-class="address"` attribute. Falls back to the line containing the Eircode. |
| `Eircode` | Regex `[ADCEFHKNPRTVWXY]\d{2}\s?[A-Z0-9]{4}` applied to the address or full body. Normalized to uppercase, no spaces. |
| `RoutingKey` | First 3 characters of the normalized Eircode. |
| `PriceMonthly` | Regex `€\s*([\d,]+(?:\.\d{2})?)\s*per\s*month` on subject first, body as fallback. |
| `Beds` | Regex `(\d+)\s*Bed\b` on body text. |
| `Baths` | Regex `(\d+)\s*Bath\b` on body text. |
| `PropertyType` | Substring match on subject: House, Apartment, Studio, Shared. Falls back to body text; defaults to "Other". |
| `BerRating` | Regex `/ber/([A-G]\d?|Exempt)\.png` on any `<img src>`. |
| `MainImageUrl` | First `<img>` whose `src` contains `media.daft.ie`. |

## Outlook SafeLinks unwrapping

Outlook wraps outgoing links as `https://<tenant>.safelinks.protection.outlook.com/?url=ENCODED&data=...`. The parser:

1. Checks the `originalsrc` attribute first — Outlook sets this to the pre-wrap URL.
2. If only `href` is present, it looks for the safelinks host. If found, it parses the query string, takes the `url` parameter, URL-decodes it, and uses that as the real URL.

See `DaftEmailParser.UnwrapSafeLink` for the implementation.

## Graceful degradation

- **No Eircode present** → `Eircode` and `RoutingKey` are both null. The property still shows in the inbox but won't be matched by routing-key filters.
- **No BER image** → `BerRating` is null. BER-minimum filter passes this property through (null is treated as "unknown but not disqualifying").
- **No main image** → `MainImageUrl` is null. Frontend falls back to a placeholder.
- **Malformed HTML** → HtmlAgilityPack's tolerant parser handles most issues; `OptionAutoCloseOnEnd` and `OptionFixNestedTags` are enabled.

## Adding a new variant

When Daft changes their template (or a new email layout appears, like Daft Sharing), the workflow is:

1. Capture a real sample email. Save it as `tests/DaftAlerts.Infrastructure.Tests/TestData/sample-<variant>.eml`.
2. Add a new `[Fact]` in `DaftEmailParserTests.cs` that asserts every field.
3. Run the tests. See what fails.
4. Adjust `DaftEmailParser.cs` — typically a new regex pattern, a new CSS selector, or a new link-walking branch.
5. Re-run the existing tests to make sure no prior variant regressed.

Because the parser is pure (takes `string`, returns `ParsedDaftEmail`), every variant is trivial to reproduce in a test. Don't skip step 3 — it's how you avoid breaking other variants.

## Re-running the parser over historical emails

Raw MIME bytes are stored in `RawEmails.RawMimeBytes` for 90 days (configurable). The `ParseRetryWorker` automatically retries rows with `ParseStatus=Failed` every 30 minutes.

To force a retry of a specific email:

```sql
UPDATE RawEmails SET ParseStatus = 2 /* Failed */, LastAttemptAt = NULL WHERE Id = <id>;
```

Within 30 minutes the worker will pick it up.

## Known limitations

- **Subject-based property type detection** can be fooled if the address contains the word "House" or "Apartment". In practice Daft's subject format is structured enough that this hasn't come up.
- **Multi-listing emails** — if a single email contains multiple listings, only the first one is parsed. The pipeline would need a list-returning overload. Not a current use case; worth watching for.
- **Non-Euro pricing** — the regex hardcodes `€`. All current Daft.ie pricing is euro; revisit if that changes.
- **`originalsrc` on non-Outlook clients** — Outlook SafeLinks sets `originalsrc` on `<a>` tags. Other clients don't wrap links at all, so `href` just works. No other client's wrapping format is handled today.
