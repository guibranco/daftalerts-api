using System;
using System.Collections.Generic;

namespace DaftAlerts.Domain.ValueObjects;

/// <summary>
/// BER (Building Energy Rating) ordinal helper. A1 is best (1), G is worst (16), Exempt = 99.
/// Used by the <c>berMin</c> filter: a property passes if its rank is &lt;= the requested minimum rank.
/// </summary>
public static class BerRank
{
    private static readonly IReadOnlyDictionary<string, int> Ranks = new Dictionary<string, int>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["A1"] = 1,
        ["A2"] = 2,
        ["A3"] = 3,
        ["B1"] = 4,
        ["B2"] = 5,
        ["B3"] = 6,
        ["C1"] = 7,
        ["C2"] = 8,
        ["C3"] = 9,
        ["D1"] = 10,
        ["D2"] = 11,
        ["E1"] = 12,
        ["E2"] = 13,
        ["F"] = 14,
        ["G"] = 15,
        ["Exempt"] = 99,
    };

    public const int UnknownRank = 100;

    public static int Rank(string? ber)
    {
        if (string.IsNullOrWhiteSpace(ber))
            return UnknownRank;
        return Ranks.TryGetValue(ber.Trim(), out var r) ? r : UnknownRank;
    }

    public static bool IsKnown(string? ber) =>
        !string.IsNullOrWhiteSpace(ber) && Ranks.ContainsKey(ber.Trim());

    public static IReadOnlyCollection<string> All => (IReadOnlyCollection<string>)Ranks.Keys;
}
