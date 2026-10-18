namespace Nexora.Application.Lookups;

/// <summary>Every typed word must occur somewhere in the item's searchable label.</summary>
public static class LookupText
{
    public static bool Matches(string? label, string? query) =>
        (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(word => (label ?? string.Empty).Contains(word, StringComparison.OrdinalIgnoreCase));
}
