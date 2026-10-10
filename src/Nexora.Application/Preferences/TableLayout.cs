using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;

namespace Nexora.Application.Preferences;

/// <summary>Saved state of one column: its stable key, position, width and whether it is shown.</summary>
public sealed record ColumnLayout(string Key, int DisplayIndex, double Width, bool Visible);

/// <summary>
/// A user's saved layout for one table. The logic lives here, outside WPF, so it can be tested:
/// merging a saved layout onto the columns a screen has today, filtering rows and adding up totals.
/// </summary>
public sealed record TableLayout(IReadOnlyList<ColumnLayout> Columns, string? SortKey, bool SortDescending)
{
    public const double MinWidth = 24;
    public const double MaxWidth = 2000;

    public string Serialize() => JsonSerializer.Serialize(this);

    /// <summary>Reads a saved layout; returns null for anything missing or unreadable, so a bad value never breaks a screen.</summary>
    public static TableLayout? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<TableLayout>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Applies a saved layout to the columns a screen defines now. Saved columns that no longer exist are dropped;
    /// new columns are appended, visible, in their default order; positions are renumbered without gaps; widths are
    /// clamped; at least one column always stays visible; a sort on a removed column is forgotten.
    /// </summary>
    public static TableLayout Merge(TableLayout? saved, IReadOnlyList<string> currentKeys, IReadOnlyList<double> defaultWidths)
    {
        ArgumentNullException.ThrowIfNull(currentKeys);
        if (defaultWidths.Count != currentKeys.Count) throw new ArgumentException("One default width per column is required.", nameof(defaultWidths));

        var known = currentKeys.ToHashSet(StringComparer.Ordinal);
        var savedColumns = (saved?.Columns ?? [])
            .Where(c => known.Contains(c.Key))
            .GroupBy(c => c.Key, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(c => c.DisplayIndex)
            .ToList();
        var ordered = savedColumns
            .Select(c => c with { Width = Clamp(c.Width) })
            .Concat(currentKeys.Select((key, i) => (key, i))
                .Where(x => savedColumns.All(c => c.Key != x.key))
                .Select(x => new ColumnLayout(x.key, 0, Clamp(defaultWidths[x.i]), true)))
            .Select((c, i) => c with { DisplayIndex = i })
            .ToList();

        if (ordered.Count > 0 && ordered.All(c => !c.Visible)) ordered[0] = ordered[0] with { Visible = true };
        var sortKey = saved?.SortKey is { } key && known.Contains(key) ? key : null;
        return new TableLayout(ordered, sortKey, sortKey is not null && saved!.SortDescending);
    }

    private static double Clamp(double width) => double.IsNaN(width) || width <= 0 ? 120 : Math.Clamp(width, MinWidth, MaxWidth);
}

public static class TableRows
{
    /// <summary>
    /// Quick filter: every word in the filter text must appear (ignoring case) in at least one of the row's cells.
    /// An empty filter matches everything.
    /// </summary>
    public static bool Matches(IEnumerable<string?> cellTexts, string? filter)
    {
        var terms = (filter ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return true;
        var cells = cellTexts.Where(c => !string.IsNullOrEmpty(c)).ToList();
        return terms.All(term => cells.Any(cell => cell!.Contains(term, StringComparison.CurrentCultureIgnoreCase)));
    }

    /// <summary>Text used for filtering a cell value: dates and numbers in the forms people type.</summary>
    public static string? CellText(object? value) => value switch
    {
        null => null,
        DateOnly d => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.##", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
        _ => value.ToString(),
    };

    /// <summary>Total of a numeric column. Nulls and non-numeric values are ignored.</summary>
    public static decimal Sum(IEnumerable<object?> values) => values.Sum(v => v switch
    {
        decimal d => d,
        int i => i,
        long l => l,
        double d => (decimal)d,
        _ => 0m,
    });
}

/// <summary>Per-user settings such as table layouts, stored under the signed-in user's id.</summary>
public sealed partial class UserPreferenceService(IWorkspaceStore store, IAuthorizer authorizer)
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var session = authorizer.RequireSession();
        return await store.GetSettingAsync(ScopedKey(session.UserId, key), cancellationToken);
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var session = authorizer.RequireSession();
        if (value.Length > 2000) throw new WorkspaceValidationException("The saved setting is too large.");
        await store.SetSettingsAsync(new Dictionary<string, string> { [ScopedKey(session.UserId, key)] = value }, cancellationToken);
    }

    private static string ScopedKey(Guid userId, string key)
    {
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException("Setting keys use lower-case letters, digits, dots and hyphens.", nameof(key));
        return $"user.{userId:N}.{key}";
    }

    [GeneratedRegex("^[a-z0-9.-]{1,50}$")]
    private static partial Regex KeyPattern();
}
