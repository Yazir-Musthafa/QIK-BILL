using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Security;
using Nexora.Printing;
using Nexora.Printing.Layout;
using Nexora.Reporting.Reports;

namespace Nexora.Reporting.Export;

public enum ExportFormat
{
    Csv,
    Excel,
    Pdf,
}

public sealed record ExportFile(string FileName, string ContentType, byte[] Content);

/// <summary>Exports and prints reports. Exporting (CSV, Excel, PDF) needs Data: Export; printing needs only the report's own permissions.</summary>
public sealed class ReportExportService(ReportService reports, PrintingService printing, IAuthorizer authorizer, IClock clock)
{
    public async Task<ExportFile> ExportAsync(string key, ReportRequest request, ExportFormat format, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.DataExport);
        var result = await reports.RunAsync(key, request with { Page = 1, PageSize = 0 }, cancellationToken);
        var table = ExportTable.From(result);
        var name = FileName(result);
        return format switch
        {
            ExportFormat.Csv => new ExportFile(name + ".csv", "text/csv", CsvWriter.Write(table, includeTitle: true)),
            ExportFormat.Excel => new ExportFile(name + ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", XlsxWriter.Write(table)),
            _ => new ExportFile(name + ".pdf", "application/pdf", (await printing.ExportPdfAsync(Subject(result), cancellationToken: cancellationToken)).Content),
        };
    }

    /// <summary>The report as a print subject (all rows), for preview and printing through the print centre.</summary>
    public async Task<PrintSubject> PrintSubjectAsync(string key, ReportRequest request, CancellationToken cancellationToken = default)
    {
        var result = await reports.RunAsync(key, request with { Page = 1, PageSize = 0 }, cancellationToken);
        return Subject(result);
    }

    private RenderedSubject Subject(ReportResult result)
    {
        var table = ExportTable.From(result);
        var local = clock.UtcNow.ToLocalTime();
        return new RenderedSubject(result.Definition.Title, "report", setup => ReportPdf.Render(table, setup, PrintFonts.Instance, local));
    }

    private static string FileName(ReportResult result)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        var title = $"{result.Definition.Title} {result.ParameterText}";
        return new string(title.Select(ch => invalid.Contains(ch) || ch == '·' ? '-' : ch).ToArray()).Replace("  ", " ", StringComparison.Ordinal).Trim();
    }
}
