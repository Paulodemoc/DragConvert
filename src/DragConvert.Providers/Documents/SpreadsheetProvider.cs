using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DragConvert.Core.Conversion;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Providers.Documents;

/// <summary>
/// CSV ↔ TSV ↔ XLSX. XLSX reads the first worksheet's values (formulas as their last calculated result; dates as
/// Excel serial numbers) and writes a single sheet with numbers stored as numbers.
/// </summary>
public sealed class SpreadsheetProvider : IConversionProvider
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public string Id => "spreadsheet";

    public bool IsAvailable => true;

    public IReadOnlyCollection<ConversionCapability> Capabilities { get; } =
    [
        new(Csv, Tsv), new(Tsv, Csv), new(Xlsx, Csv), new(Xlsx, Tsv), new(Csv, Xlsx), new(Tsv, Xlsx),
    ];

    public Task ConvertAsync(ConversionJob job, IProgress<double>? progress, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            MediaFormat input = FromPath(job.InputPath)!;
            IReadOnlyList<IReadOnlyList<string>> rows;
            try
            {
                rows = input == Xlsx ? ReadXlsx(job.InputPath) : ParseDelimited(File.ReadAllText(job.InputPath), input == Tsv ? '\t' : DetectCsvDelimiter(job.InputPath));
            }
            catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or FileFormatException)
            {
                throw new ConversionException($"{Path.GetFileName(job.InputPath)} isn't a readable spreadsheet.", ex);
            }

            cancellationToken.ThrowIfCancellationRequested();
            switch (job.Target.Id)
            {
                case "xlsx":
                    WriteXlsx(job.OutputPath, rows);
                    break;
                case "tsv":
                    File.WriteAllText(job.OutputPath, WriteDelimited(rows, '\t'), Utf8);
                    break;
                default:
                    // UTF-8 with BOM so Excel opens non-ASCII text correctly.
                    File.WriteAllText(job.OutputPath, WriteDelimited(rows, ','), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                    break;
            }
        },
        cancellationToken);

    /// <summary>RFC 4180 parsing: quoted fields may contain the delimiter, quotes ("") and line breaks.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> ParseDelimited(string text, char delimiter)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    public static string WriteDelimited(IReadOnlyList<IReadOnlyList<string>> rows, char delimiter)
    {
        var sb = new StringBuilder();
        foreach (IReadOnlyList<string> row in rows)
        {
            sb.AppendJoin(delimiter, row.Select(field => delimiter == '\t'
                ? field.Replace('\t', ' ').Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ')
                : field.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : field));
            sb.Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>Semicolon-separated "CSV" (common in European Excel exports) when the first line has more semicolons than commas.</summary>
    private static char DetectCsvDelimiter(string path)
    {
        string first = File.ReadLines(path).FirstOrDefault() ?? string.Empty;
        return first.Count(c => c == ';') > first.Count(c => c == ',') ? ';' : ',';
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadXlsx(string path)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, isEditable: false);
        WorkbookPart workbook = document.WorkbookPart ?? throw new ConversionException("The workbook is empty.");
        Sheet sheet = workbook.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault() ?? throw new ConversionException("The workbook has no sheets.");
        var worksheet = (WorksheetPart)workbook.GetPartById(sheet.Id!.Value!);
        SharedStringItem[] shared = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().ToArray() ?? [];

        var rows = new List<IReadOnlyList<string>>();
        foreach (Row row in worksheet.Worksheet?.Descendants<Row>() ?? [])
        {
            var values = new List<string>();
            foreach (Cell cell in row.Elements<Cell>())
            {
                int column = ColumnIndex(cell.CellReference?.Value);
                while (values.Count < column)
                {
                    values.Add(string.Empty);
                }

                values.Add(CellText(cell, shared));
            }

            int index = row.RowIndex is { } r ? (int)r.Value - 1 : rows.Count;
            while (rows.Count < index)
            {
                rows.Add([]);
            }

            rows.Add(values);
        }

        return rows;
    }

    private static string CellText(Cell cell, SharedStringItem[] shared)
    {
        string raw = cell.CellValue?.Text ?? string.Empty;
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(raw, out int i) && i < shared.Length)
        {
            return shared[i].InnerText;
        }

        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText ?? string.Empty;
        }

        if (cell.DataType?.Value == CellValues.Boolean)
        {
            return raw == "1" ? "TRUE" : "FALSE";
        }

        return raw;
    }

    /// <summary>Zero-based column index from a reference such as "C7"; -1 when absent (cells then just follow on).</summary>
    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return -1;
        }

        int index = 0;
        foreach (char c in reference.TakeWhile(char.IsLetter))
        {
            index = (index * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return index - 1;
    }

    private static void WriteXlsx(string path, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        WorkbookPart workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        WorksheetPart worksheet = workbook.AddNewPart<WorksheetPart>();
        var data = new SheetData();
        uint rowIndex = 1;
        foreach (IReadOnlyList<string> values in rows)
        {
            var row = new Row { RowIndex = rowIndex++ };
            foreach (string value in values)
            {
                row.Append(double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) && value.Trim().Length == value.Length
                    ? new Cell { DataType = CellValues.Number, CellValue = new CellValue(value) }
                    : new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve }) });
            }

            data.Append(row);
        }

        worksheet.Worksheet = new Worksheet(data);
        workbook.Workbook.AppendChild(new Sheets(new Sheet { Id = workbook.GetIdOfPart(worksheet), SheetId = 1, Name = "Sheet1" }));
        workbook.Workbook.Save();
    }
}
