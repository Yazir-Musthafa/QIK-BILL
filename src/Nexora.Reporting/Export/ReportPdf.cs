using System.Globalization;
using Nexora.Application.Formatting;
using Nexora.Domain.Printing;
using Nexora.Printing.Layout;
using Nexora.Reporting.Reports;

namespace Nexora.Reporting.Export;

/// <summary>Lays out a table (report or export) on pages with the Phase 6 page composer, so it prints and saves as PDF like other documents.</summary>
public static class ReportPdf
{
    public static RenderedDocument Render(ExportTable table, PageSetup setup, ITextMeasurer measure, DateTime generatedLocal)
    {
        // Wide tables use landscape on sheet paper.
        if (table.Columns.Count > 7 && !setup.IsRoll && setup.Orientation == PageOrientation.Portrait) setup = setup with { Orientation = PageOrientation.Landscape };
        var c = new PageComposer(setup, measure);
        var f = Math.Max(6, c.FontSize - (table.Columns.Count > 10 ? 2 : table.Columns.Count > 7 ? 1 : 0));
        // A report whose only text is its first column (statements, summaries) gives that column the room it needs.
        var textColumns = table.Columns.Count(col => col.Kind == ColumnKind.Text);
        var weights = table.Columns.Select((col, i) => col.Kind switch
        {
            ColumnKind.Text => i == 0 && textColumns == 1 ? 3.6 : 2.2,
            ColumnKind.Date => 1.1,
            _ => 1.5,
        }).ToArray();
        var unit = c.Width / weights.Sum();
        var x = new double[table.Columns.Count];
        for (var i = 1; i < x.Length; i++) x[i] = x[i - 1] + weights[i - 1] * unit;
        double W(int i) => weights[i] * unit - 3;

        var titles = table.TitleLines.Where(t => t.Length > 0).ToList();
        if (titles.Count > 0) c.Move(c.Wrapped(0, c.Width, titles[0], f + 2, bold: true));
        if (titles.Count > 1) c.Move(c.Wrapped(0, c.Width, titles[1], f + 1, bold: true));
        foreach (var line in titles.Skip(2)) c.Move(c.Wrapped(0, c.Width, line, f - 0.5));
        c.Move(c.Wrapped(0, c.Width, $"Generated {generatedLocal:dd-MM-yyyy HH:mm} by QIK Suite", f - 1));
        c.Move(4);

        void Header(PageComposer composer)
        {
            var h = composer.LineHeight(f) + 2;
            composer.Ensure(h);
            for (var i = 0; i < table.Columns.Count; i++)
                composer.Text(x[i], W(i), TextFit.Truncate(measure, table.Columns[i].Header, W(i), f, true), f, true, table.Columns[i].IsNumeric ? TextAlign.Right : TextAlign.Left);
            composer.Move(h);
            composer.Rule(0.6);
            composer.Move(2);
        }

        Header(c);
        c.ContinuationHeader = Header;
        void Row(IReadOnlyList<object?> cells, bool bold, int indent)
        {
            c.Ensure(c.LineHeight(f));
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var text = Format(i < cells.Count ? cells[i] : null, table.Columns[i].Kind);
                if (text.Length == 0) continue;
                var shift = i == 0 ? indent * 8 : 0;
                c.Text(x[i] + shift, W(i) - shift, TextFit.Truncate(measure, text, W(i) - shift, f, bold), f, bold, table.Columns[i].IsNumeric ? TextAlign.Right : TextAlign.Left);
            }

            c.Move(c.LineHeight(f));
        }

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var (bold, indent) = table.Style(r);
            Row(table.Rows[r], bold, indent);
        }
        if (table.Rows.Count == 0) c.Move(c.Wrapped(0, c.Width, "No rows for these parameters.", f));
        if (table.Totals is { } totals)
        {
            c.Ensure(c.LineHeight(f) + 5);
            c.Move(2);
            c.Rule(0.6);
            c.Move(2);
            Row(totals.Select((v, i) => i == 0 && v is null ? (object?)"Total" : v).ToList(), true, 0);
        }

        return c.Finish(table.Name);
    }

    /// <summary>Screen and paper formatting: Indian digit grouping, dd-MM-yyyy dates.</summary>
    public static string Format(object? cell, ColumnKind kind) => cell switch
    {
        null => string.Empty,
        DateOnly d => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
        decimal m when kind == ColumnKind.Quantity => m.ToString("0.###", CultureInfo.InvariantCulture),
        decimal m when kind == ColumnKind.Percent => m.ToString("0.##", CultureInfo.InvariantCulture) + "%",
        decimal m => IndianNumberFormat.Number(m),
        int n => n.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(cell, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
