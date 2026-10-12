using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Nexora.Reporting.Reports;

namespace Nexora.Reporting.Export;

/// <summary>A plain table to export: title lines, typed columns, rows and an optional totals row.</summary>
public sealed record ExportTable(string Name, IReadOnlyList<string> TitleLines, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows,
    IReadOnlyList<object?>? Totals = null, IReadOnlyList<(bool Emphasis, int Indent)>? Styles = null)
{
    public static ExportTable From(ReportResult r) => new(r.Definition.Title,
        [r.CompanyName, r.Definition.Title, r.ParameterText, .. r.Notes],
        r.Columns, r.Rows.Select(x => x.Cells).ToList(), r.Totals, r.Rows.Select(x => (x.Emphasis, x.Indent)).ToList());

    public (bool Emphasis, int Indent) Style(int row) => Styles is { } s && row < s.Count ? s[row] : (false, 0);
}

/// <summary>
/// Protects spreadsheet exports against formula injection: a text cell starting with =, +, -, @, tab or carriage return
/// is prefixed with an apostrophe so spreadsheet programs show it as text instead of running it. Numbers are written as
/// numbers, so negative amounts are never affected.
/// </summary>
public static class SpreadsheetSafety
{
    private static readonly char[] Triggers = ['=', '+', '-', '@', '\t', '\r'];

    public static string Sanitize(string text) => text.Length > 0 && Triggers.Contains(text[0]) ? "'" + text : text;
}

public static class CsvWriter
{
    /// <summary>UTF-8 with a byte-order mark (so Excel reads ₹ and Indian names correctly). Dates are yyyy-MM-dd; numbers are plain, without grouping.</summary>
    public static byte[] Write(ExportTable table, bool includeTitle = false)
    {
        var sb = new StringBuilder();
        void Line(IEnumerable<string> cells) => sb.Append(string.Join(',', cells.Select(Quote))).Append("\r\n");
        if (includeTitle)
        {
            foreach (var title in table.TitleLines.Where(t => t.Length > 0)) Line([SpreadsheetSafety.Sanitize(title)]);
            sb.Append("\r\n");
        }

        Line(table.Columns.Select(c => SpreadsheetSafety.Sanitize(c.Header)));
        foreach (var row in table.Rows) Line(row.Select((cell, i) => Format(cell, table.Columns[i].Kind)));
        if (table.Totals is { } totals) Line(totals.Select((cell, i) => i == 0 && cell is null ? "Total" : Format(cell, table.Columns[i].Kind)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    internal static string Format(object? cell, ColumnKind kind) => cell switch
    {
        null => string.Empty,
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal m => m.ToString(kind == ColumnKind.Quantity ? "0.###" : "0.00", CultureInfo.InvariantCulture),
        int n => n.ToString(CultureInfo.InvariantCulture),
        string s => SpreadsheetSafety.Sanitize(s),
        _ => SpreadsheetSafety.Sanitize(Convert.ToString(cell, CultureInfo.InvariantCulture) ?? string.Empty),
    };

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}

/// <summary>Writes one worksheet with real numbers and dates (Indian digit grouping as a number format), built with the Open XML SDK.</summary>
public static class XlsxWriter
{
    private const uint Bold = 1, Amount = 2, Quantity = 3, DateStyle = 4, BoldAmount = 5, Percent = 6, Integer = 7, BoldQuantity = 8;

    public static byte[] Write(ExportTable table, bool includeTitle = true)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var styles = workbook.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = Stylesheet();
            var sheetPart = workbook.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            uint rowIndex = 0;
            Row NewRow() => new() { RowIndex = ++rowIndex };

            if (includeTitle)
            {
                foreach (var title in table.TitleLines.Where(t => t.Length > 0))
                {
                    var r = NewRow();
                    r.Append(TextCell(title, rowIndex == 1 || rowIndex == 2 ? Bold : 0));
                    data.Append(r);
                }

                data.Append(NewRow());
            }

            var header = NewRow();
            foreach (var column in table.Columns) header.Append(TextCell(column.Header, Bold));
            data.Append(header);
            var firstDataRow = rowIndex + 1;
            for (var index = 0; index < table.Rows.Count; index++)
            {
                var cells = table.Rows[index];
                var (bold, indent) = table.Style(index);
                var r = NewRow();
                for (var i = 0; i < table.Columns.Count; i++)
                {
                    var value = i < cells.Count ? cells[i] : null;
                    if (i == 0 && indent > 0 && value is string text) value = new string(' ', indent * 4) + text;
                    r.Append(Cell(value, table.Columns[i].Kind, bold));
                }

                data.Append(r);
            }

            if (table.Totals is { } totals)
            {
                var r = NewRow();
                for (var i = 0; i < table.Columns.Count; i++) r.Append(i == 0 && totals[i] is null ? TextCell("Total", Bold) : Cell(totals[i], table.Columns[i].Kind, true));
                data.Append(r);
            }

            var columns = new Columns();
            for (var i = 0; i < table.Columns.Count; i++)
                columns.Append(new Column { Min = (uint)i + 1, Max = (uint)i + 1, Width = table.Columns[i].IsNumeric ? 16 : table.Columns[i].Kind == ColumnKind.Date ? 12 : 28, CustomWidth = true });
            var worksheet = new Worksheet();
            worksheet.Append(new SheetViews(new SheetView(new Pane { VerticalSplit = firstDataRow - 1, TopLeftCell = "A" + firstDataRow, State = PaneStateValues.Frozen,
                ActivePane = PaneValues.BottomLeft }) { WorkbookViewId = 0 }));
            worksheet.Append(columns);
            worksheet.Append(data);
            sheetPart.Worksheet = worksheet;
            var sheets = workbook.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = workbook.GetIdOfPart(sheetPart), SheetId = 1, Name = SheetName(table.Name) });
            workbook.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static Cell Cell(object? value, ColumnKind kind, bool bold) => value switch
    {
        null => new Cell(),
        decimal m => Number(m.ToString(CultureInfo.InvariantCulture), kind switch
        {
            ColumnKind.Quantity => bold ? BoldQuantity : Quantity,
            ColumnKind.Percent => Percent,
            ColumnKind.Integer => Integer,
            _ => bold ? BoldAmount : Amount,
        }),
        int n => Number(n.ToString(CultureInfo.InvariantCulture), Integer),
        DateOnly d => Number(d.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture), DateStyle),
        _ => TextCell(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, bold ? Bold : 0),
    };

    private static Cell Number(string text, uint style) => new() { CellValue = new CellValue(text), DataType = CellValues.Number, StyleIndex = style };

    private static Cell TextCell(string text, uint style) => new(new InlineString(new Text(SpreadsheetSafety.Sanitize(text)) { Space = SpaceProcessingModeValues.Preserve }))
    {
        DataType = CellValues.InlineString,
        StyleIndex = style,
    };

    private static string SheetName(string name)
    {
        var clean = new string(name.Where(c => !"[]:*?/\\".Contains(c)).ToArray()).Trim();
        return clean.Length == 0 ? "Sheet1" : clean[..Math.Min(31, clean.Length)];
    }

    private static Stylesheet Stylesheet()
    {
        // Indian grouping: 1,00,00,000.00 for crores, 1,00,000.00 for lakhs, 10,000.00 below.
        const string indian = @"[>=10000000]##\,##\,##\,##0.00;[>=100000]##\,##\,##0.00;##,##0.00";
        var formats = new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164, FormatCode = indian },
            new NumberingFormat { NumberFormatId = 165, FormatCode = "0.###" },
            new NumberingFormat { NumberFormatId = 166, FormatCode = "dd-mm-yyyy" },
            new NumberingFormat { NumberFormatId = 167, FormatCode = "0.00" }) { Count = 4 };
        var fonts = new Fonts(new Font(new FontSize { Val = 11 }), new Font(new DocumentFormat.OpenXml.Spreadsheet.Bold(), new FontSize { Val = 11 })) { Count = 2 };
        var fills = new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 };
        var borders = new Borders(new Border()) { Count = 1 };
        CellFormat F(uint format, uint font) => new() { NumberFormatId = format, FontId = font, FillId = 0, BorderId = 0, ApplyNumberFormat = format != 0, ApplyFont = font != 0 };
        var cellFormats = new CellFormats(F(0, 0), F(0, 1), F(164, 0), F(165, 0), F(166, 0), F(164, 1), F(167, 0), F(1, 0), F(165, 1)) { Count = 9 };
        return new Stylesheet(formats, fonts, fills, borders, cellFormats);
    }
}
