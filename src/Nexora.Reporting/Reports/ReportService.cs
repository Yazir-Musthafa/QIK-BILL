using System.Globalization;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Companies;
using Nexora.Domain.Reports;
using Nexora.Domain.Security;

namespace Nexora.Reporting.Reports;

/// <summary>What a generator produces. Generators that page at the source return <see cref="TotalRows"/> and <see cref="Totals"/> for the whole selection.</summary>
internal sealed record ReportData(IReadOnlyList<ReportColumn> Columns, IReadOnlyList<ReportRow> Rows, string ParameterText,
    IReadOnlyList<object?>? Totals = null, int? TotalRows = null, IReadOnlyList<string>? Notes = null);

internal sealed record ReportContext(IUnitOfWork Unit, ReportRequest Request, SessionInfo Session, Company Company, ReportSettings Settings, DateOnly Today)
{
    public bool CanSeeCost => Session.Has(Permissions.InventoryViewCost);

    public DateOnly From => Request.From ?? new DateOnly(Today.Year, Today.Month, 1);

    public DateOnly To => Request.To ?? Today;

    public DateOnly AsOf => Request.AsOf ?? Today;

    public string RangeText => $"{From:dd-MM-yyyy} to {To:dd-MM-yyyy}";

    public string AsOfText => $"As of {AsOf:dd-MM-yyyy}";

    public void EnsureRange()
    {
        if (To < From) throw new WorkspaceValidationException("The end date must be on or after the start date.");
        if (To.DayNumber - From.DayNumber > 3660) throw new WorkspaceValidationException("Choose a range of at most ten years.");
    }
}

internal interface IReportGenerator
{
    string Key { get; }

    Task<ReportData> RunAsync(ReportContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Runs reports. Every report reads posted data through database queries; this class checks permissions, removes cost
/// columns for users without Inventory: View cost, and applies search, sort, totals and paging (in the database for
/// reports marked <see cref="ReportDefinition.PagedAtSource"/>).
/// </summary>
public sealed class ReportService(IUnitOfWorkFactory units, IAuthorizer authorizer, IClock clock, IEnumerable<IReportGeneratorMarker> generators)
{
    private readonly Dictionary<string, IReportGenerator> _generators = generators.Cast<IReportGenerator>().ToDictionary(g => g.Key);

    public const string GstNote = "This is a plain summary of the GST QIK Suite recorded. It is not a GST return and QIK Suite does not connect to the GST portal; check it before using it to file. Cess is not calculated by QIK Suite and shows as 0.";

    /// <summary>The reports this user may open.</summary>
    public IReadOnlyList<ReportDefinition> Available()
    {
        var session = authorizer.Demand(Permissions.ReportsView);
        return ReportCatalog.All.Where(d => d.Permissions.All(session.Has) && _generators.ContainsKey(d.Key)).ToList();
    }

    public async Task<ReportResult> RunAsync(string key, ReportRequest request, CancellationToken cancellationToken = default)
    {
        var definition = ReportCatalog.Find(key);
        var session = authorizer.Demand(Permissions.ReportsView);
        foreach (var permission in definition.Permissions) authorizer.Demand(permission);
        if (!_generators.TryGetValue(key, out var generator)) throw new WorkspaceValidationException($"{definition.Title} is not available.");
        if (request.PageSize < 0 || request.Page < 1) throw new WorkspaceValidationException("Choose a page from 1 on.");

        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var company = await unit.FindAsync<Company>(session.CompanyId, cancellationToken) ?? throw new WorkspaceValidationException("Sign in again.");
        var settings = (await unit.ListAsync<ReportSettings>(cancellationToken: cancellationToken)).SingleOrDefault() ?? new ReportSettings(Guid.NewGuid(), company.Id);
        var today = DateOnly.FromDateTime(clock.UtcNow.ToLocalTime());
        var context = new ReportContext(unit, request, session, company, settings, today);
        var data = await generator.RunAsync(context, cancellationToken);

        var notes = new List<string>(data.Notes ?? []);
        if (company.IsDemo) notes.Insert(0, "Demonstration company: these figures come from sample data, not a real business.");
        if (definition.IsGst) notes.Add(GstNote);

        // Cost and profit columns never leave this service for users without Inventory: View cost.
        var keep = Enumerable.Range(0, data.Columns.Count).Where(i => context.CanSeeCost || !data.Columns[i].IsCost).ToArray();
        if (keep.Length != data.Columns.Count) notes.Add("Cost and profit columns are hidden: your role does not have Inventory: View cost.");
        var columns = keep.Select(i => data.Columns[i]).ToList();
        IReadOnlyList<object?> Pick(IReadOnlyList<object?> cells) => keep.Select(i => i < cells.Count ? cells[i] : null).ToList();
        var rows = data.Rows.Select(r => r with { Cells = Pick(r.Cells) }).ToList();
        var totals = data.Totals is null ? null : Pick(data.Totals);

        int totalRows;
        if (definition.PagedAtSource)
        {
            totalRows = data.TotalRows ?? rows.Count;
        }
        else
        {
            rows = Search(rows, request.Search).ToList();
            rows = Sort(rows, columns, request.SortKey, request.SortDescending);
            totalRows = rows.Count;
            totals ??= Totals(columns, rows);
            if (request.PageSize > 0) rows = rows.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList();
        }

        return new ReportResult(definition, company.Name, data.ParameterText, columns, rows, totals, totalRows, request.Page, request.PageSize, notes,
            company.IsDemo, clock.UtcNow);
    }

    internal static IEnumerable<ReportRow> Search(IEnumerable<ReportRow> rows, string? search)
    {
        var words = (search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return rows;
        return rows.Where(r =>
        {
            var text = string.Join(' ', r.Cells.Select(CellText));
            return words.All(w => text.Contains(w, StringComparison.CurrentCultureIgnoreCase));
        });
    }

    internal static List<ReportRow> Sort(List<ReportRow> rows, IReadOnlyList<ReportColumn> columns, string? sortKey, bool descending)
    {
        var index = columns.ToList().FindIndex(c => c.Key == sortKey);
        if (index < 0) return rows;
        var comparer = Comparer<object?>.Create(CompareCells);
        return (descending ? rows.OrderByDescending(r => r.Cells[index], comparer) : rows.OrderBy(r => r.Cells[index], comparer)).ToList();
    }

    internal static IReadOnlyList<object?>? Totals(IReadOnlyList<ReportColumn> columns, IReadOnlyList<ReportRow> rows)
    {
        if (!columns.Any(c => c.Total)) return null;
        return columns.Select((c, i) => c.Total ? (object?)rows.Where(r => !r.Emphasis).Sum(r => r.Cells[i] is decimal d ? d : r.Cells[i] is int n ? n : 0m) : null).ToList();
    }

    internal static string CellText(object? cell) => cell switch
    {
        null => string.Empty,
        DateOnly d => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.00", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => cell.ToString() ?? string.Empty,
    };

    private static int CompareCells(object? a, object? b) => (a, b) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        (decimal x, decimal y) => x.CompareTo(y),
        (int x, int y) => x.CompareTo(y),
        (DateOnly x, DateOnly y) => x.CompareTo(y),
        _ => string.Compare(CellText(a), CellText(b), StringComparison.CurrentCultureIgnoreCase),
    };
}

/// <summary>Registration handle for report generators (the generators themselves are internal).</summary>
public interface IReportGeneratorMarker;
