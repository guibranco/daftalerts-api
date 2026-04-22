using System;
using System.Text.RegularExpressions;

namespace DaftAlerts.Domain.ValueObjects;

/// <summary>
/// Irish Eircode — official format: <c>[ADCEFHKNPRTVWXY]\d{2}[ ]?[A-Z0-9]{4}</c>.
/// Case-insensitive on input; normalized to uppercase, no spaces.
/// </summary>
public readonly record struct Eircode
{
    private static readonly Regex EircodePattern = new(
        @"^[ADCEFHKNPRTVWXY]\d{2}\s?[A-Z0-9]{4}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public static readonly Regex ExtractPattern = new(
        @"\b([ADCEFHKNPRTVWXY]\d{2}\s?[A-Z0-9]{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public string Value { get; }
    public string RoutingKey => Value[..3];

    private Eircode(string value)
    {
        Value = value;
    }

    public static bool TryParse(string? input, out Eircode eircode)
    {
        eircode = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var trimmed = input.Trim();
        if (!EircodePattern.IsMatch(trimmed))
            return false;

        var normalized = trimmed
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        eircode = new Eircode(normalized);
        return true;
    }

    public static Eircode Parse(string input)
    {
        if (!TryParse(input, out var eircode))
            throw new FormatException($"'{input}' is not a valid Eircode.");
        return eircode;
    }

    /// <summary>Extracts the first Eircode found anywhere in <paramref name="text"/>, or null.</summary>
    public static Eircode? Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = ExtractPattern.Match(text);
        if (!match.Success)
            return null;
        return TryParse(match.Groups[1].Value, out var e) ? e : null;
    }

    public override string ToString() => Value;
}
