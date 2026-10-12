using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Nexora.Application;

namespace Nexora.Reporting.Import;

public sealed record TabularRow(int RowNumber, IReadOnlyList<string> Cells);

public sealed record TabularData(IReadOnlyList<string> Headers, IReadOnlyList<TabularRow> Rows);

/// <summary>Reads the first sheet of an Excel workbook or a CSV file into text cells. The first non-empty row is the header.</summary>
public static class TabularReader
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const int MaxRows = 20_000;

    public static TabularData Read(byte[] content, string fileName)
    {
        if (content.Length == 0) throw new WorkspaceValidationException("The file is empty.");
        if (content.Length > MaxBytes) throw new WorkspaceValidationException("Import files can be at most 10 MB.");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var isZip = content.Length > 4 && content[0] == 'P' && content[1] == 'K';
        var rows = extension switch
        {
            ".xlsx" when isZip => ReadXlsx(content),
            ".xlsx" => throw new WorkspaceValidationException("This file is not a valid Excel workbook (.xlsx)."),
            ".csv" or ".txt" => ReadCsv(content),
            _ => throw new WorkspaceValidationException("Choose a .csv or .xlsx file."),
        };

        var nonEmpty = rows.Where(r => r.Cells.Any(c => c.Length > 0)).ToList();
        if (nonEmpty.Count == 0) throw new WorkspaceValidationException("The file has no rows.");
        if (nonEmpty.Count - 1 > MaxRows) throw new WorkspaceValidationException($"Import at most {MaxRows:N0} rows at a time.");
        var header = nonEmpty[0].Cells.Select(c => c.Trim()).ToList();
        return new TabularData(header, nonEmpty.Skip(1).ToList());
    }

    private static List<TabularRow> ReadCsv(byte[] content)
    {
        var text = new UTF8Encoding(false, false).GetString(content);
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var rows = new List<TabularRow>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var line = 1;
        var rowStart = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else { if (ch == '\n') line++; cell.Append(ch); }
                continue;
            }

            switch (ch)
            {
                case '"' when cell.Length == 0: quoted = true; break;
                case ',': cells.Add(cell.ToString()); cell.Clear(); break;
                case '\r': break;
                case '\n':
                    cells.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(new TabularRow(rowStart, cells));
                    cells = [];
                    line++;
                    rowStart = line;
                    break;
                default: cell.Append(ch); break;
            }
        }

        if (quoted) throw new WorkspaceValidationException($"Row {rowStart}: a quoted value is not closed.");
        if (cell.Length > 0 || cells.Count > 0)
        {
            cells.Add(cell.ToString());
            rows.Add(new TabularRow(rowStart, cells));
        }

        return rows;
    }

    private static List<TabularRow> ReadXlsx(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content);
            using var document = SpreadsheetDocument.Open(stream, false);
            var workbook = document.WorkbookPart ?? throw new WorkspaceValidationException("The workbook has no sheets.");
            var sheet = workbook.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault() ?? throw new WorkspaceValidationException("The workbook has no sheets.");
            var part = (WorksheetPart)workbook.GetPartById(sheet.Id!.Value!);
            var shared = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(s => s.InnerText).ToList() ?? [];
            var dateStyles = DateStyles(workbook);
            var rows = new List<TabularRow>();
            foreach (var row in part.Worksheet!.Descendants<Row>())
            {
                var values = new List<string>();
                foreach (var c in row.Elements<Cell>())
                {
                    var column = ColumnIndex(c.CellReference?.Value);
                    while (values.Count < column) values.Add(string.Empty);
                    values.Add(Text(c, shared, dateStyles));
                }

                rows.Add(new TabularRow((int)(row.RowIndex?.Value ?? (uint)rows.Count + 1), values));
                if (rows.Count > MaxRows + 50) break;
            }

            return rows;
        }
        catch (WorkspaceValidationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new WorkspaceValidationException("The Excel file could not be read. Save it again as an Excel workbook (.xlsx) and retry.");
        }
    }

    private static string Text(Cell c, List<string> shared, HashSet<uint> dateStyles)
    {
        var raw = c.CellValue?.Text ?? c.InlineString?.InnerText ?? string.Empty;
        if (c.DataType?.Value == CellValues.SharedString) return int.TryParse(raw, out var i) && i < shared.Count ? shared[i] : string.Empty;
        if (c.DataType?.Value == CellValues.InlineString) return c.InlineString?.InnerText ?? raw;
        if (c.DataType?.Value == CellValues.Boolean) return raw == "1" ? "Yes" : "No";
        if ((c.DataType is null || c.DataType.Value == CellValues.Number) && c.StyleIndex is { } style && dateStyles.Contains(style.Value)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var oa))
            return DateOnly.FromDateTime(DateTime.FromOADate(oa)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return raw;
    }

    /// <summary>Cell formats that show a date: the built-in date formats and custom ones containing day, month and year.</summary>
    private static HashSet<uint> DateStyles(WorkbookPart workbook)
    {
        var result = new HashSet<uint>();
        var stylesheet = workbook.WorkbookStylesPart?.Stylesheet;
        if (stylesheet?.CellFormats is null) return result;
        var custom = stylesheet.NumberingFormats?.Elements<NumberingFormat>()
            .Where(f => f.FormatCode?.Value is { } code && code.Contains('y', StringComparison.OrdinalIgnoreCase) && code.Contains('d', StringComparison.OrdinalIgnoreCase))
            .Select(f => f.NumberFormatId!.Value).ToHashSet() ?? [];
        uint index = 0;
        foreach (var format in stylesheet.CellFormats.Elements<CellFormat>())
        {
            var id = format.NumberFormatId?.Value ?? 0;
            if (id is >= 14 and <= 22 || custom.Contains(id)) result.Add(index);
            index++;
        }

        return result;
    }

    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return 0;
        var index = 0;
        foreach (var ch in reference.TakeWhile(char.IsLetter)) index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        return Math.Max(0, index - 1);
    }
}
