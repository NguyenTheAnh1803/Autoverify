using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace AutoVerify.Api.Services;

/// <summary>
/// Lightweight, dependency-free Excel (.xlsx) and CSV/TSV parser using .NET ZipArchive and XDocument.
/// Extracts worksheet names, headers, and rows.
/// </summary>
public static class ExcelSheetReader
{
    public sealed record ParsedWorksheet(
        string SheetName,
        List<string> Headers,
        List<Dictionary<string, string>> Rows
    );

    /// <summary>
    /// Reads all worksheets from an Excel (.xlsx) file stream.
    /// </summary>
    public static List<ParsedWorksheet> ReadXlsx(Stream stream)
    {
        var results = new List<ParsedWorksheet>();

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        // 1. Read Shared Strings (xl/sharedStrings.xml)
        var sharedStrings = new List<string>();
        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry != null)
        {
            using var sStream = sharedStringsEntry.Open();
            var xDoc = XDocument.Load(sStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach (var si in xDoc.Descendants(ns + "si"))
            {
                // Can be direct <t> or nested in <r><t>
                var t = string.Concat(si.Descendants(ns + "t").Select(x => x.Value));
                sharedStrings.Add(t);
            }
        }

        // 2. Read Workbook to get Sheet Names and relation IDs (xl/workbook.xml)
        var sheets = new List<(string Name, string SheetId, string RId)>();
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry == null) return results;

        using (var wbStream = workbookEntry.Open())
        {
            var xDoc = XDocument.Load(wbStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace rNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

            foreach (var sheet in xDoc.Descendants(ns + "sheet"))
            {
                var name = sheet.Attribute("name")?.Value ?? "Sheet";
                var sheetId = sheet.Attribute("sheetId")?.Value ?? "1";
                var rId = sheet.Attribute(rNs + "id")?.Value ?? "";
                sheets.Add((name, sheetId, rId));
            }
        }

        // 3. Read Workbook Relationships (xl/_rels/workbook.xml.rels)
        var relMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry != null)
        {
            using var relStream = relsEntry.Open();
            var xDoc = XDocument.Load(relStream);
            XNamespace prNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            foreach (var rel in xDoc.Descendants(prNs + "Relationship"))
            {
                var id = rel.Attribute("Id")?.Value;
                var target = rel.Attribute("Target")?.Value;
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                {
                    relMap[id] = target.StartsWith('/') ? target[1..] : (target.StartsWith("xl/") ? target : "xl/" + target);
                }
            }
        }

        // 4. Read each Worksheet
        for (int sIdx = 0; sIdx < sheets.Count; sIdx++)
        {
            var (sheetName, _, rId) = sheets[sIdx];
            var targetPath = relMap.TryGetValue(rId, out var mappedPath)
                ? mappedPath
                : $"xl/worksheets/sheet{sIdx + 1}.xml";

            var sheetEntry = archive.GetEntry(targetPath) ?? archive.GetEntry($"xl/worksheets/sheet{sIdx + 1}.xml");
            if (sheetEntry == null) continue;

            using var sheetStream = sheetEntry.Open();
            var xDoc = XDocument.Load(sheetStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var sheetData = xDoc.Descendants(ns + "sheetData").FirstOrDefault();
            if (sheetData == null) continue;

            var rowsXml = sheetData.Elements(ns + "row").ToList();
            if (rowsXml.Count == 0) continue;

            // Extract cells for each row
            var parsedRows = new List<List<string>>();
            foreach (var r in rowsXml)
            {
                var rowCells = new SortedDictionary<int, string>();
                foreach (var c in r.Elements(ns + "c"))
                {
                    var cellRef = c.Attribute("r")?.Value ?? "";
                    var colIndex = ColumnNameToIndex(cellRef);
                    var cellType = c.Attribute("t")?.Value;
                    var valElem = c.Element(ns + "v");
                    var rawVal = valElem?.Value ?? "";

                    string val;
                    if (cellType == "s" && int.TryParse(rawVal, out var sIdxVal) && sIdxVal >= 0 && sIdxVal < sharedStrings.Count)
                    {
                        val = sharedStrings[sIdxVal];
                    }
                    else if (cellType == "inlineStr")
                    {
                        val = c.Element(ns + "is")?.Element(ns + "t")?.Value ?? "";
                    }
                    else
                    {
                        val = rawVal;
                    }

                    rowCells[colIndex] = val;
                }

                if (rowCells.Count > 0)
                {
                    var maxCol = rowCells.Keys.Max();
                    var fullRow = new List<string>(maxCol + 1);
                    for (int col = 0; col <= maxCol; col++)
                    {
                        fullRow.Add(rowCells.TryGetValue(col, out var cv) ? cv.Trim() : string.Empty);
                    }
                    parsedRows.Add(fullRow);
                }
            }

            if (parsedRows.Count == 0) continue;

            // First row as headers
            var headerRow = parsedRows[0];
            var headers = new List<string>(headerRow.Count);
            for (int h = 0; h < headerRow.Count; h++)
            {
                var hName = headerRow[h].Trim();
                if (string.IsNullOrWhiteSpace(hName)) hName = $"Column_{h + 1}";
                headers.Add(hName);
            }

            var rowDicts = new List<Dictionary<string, string>>();
            for (int r = 1; r < parsedRows.Count; r++)
            {
                var rData = parsedRows[r];
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int h = 0; h < headers.Count; h++)
                {
                    dict[headers[h]] = h < rData.Count ? rData[h] : string.Empty;
                }
                // Skip completely empty rows
                if (dict.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
                {
                    rowDicts.Add(dict);
                }
            }

            results.Add(new ParsedWorksheet(sheetName, headers, rowDicts));
        }

        return results;
    }

    /// <summary>
    /// Reads CSV / TSV text content as a worksheet.
    /// </summary>
    public static ParsedWorksheet ReadDelimitedText(string text, string sheetName = "Data")
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return new ParsedWorksheet(sheetName, [], []);

        // Detect delimiter: comma, tab, or semicolon
        var firstLine = lines[0];
        char delimiter = ',';
        if (firstLine.Contains('\t')) delimiter = '\t';
        else if (firstLine.Contains(';') && !firstLine.Contains(',')) delimiter = ';';

        var rawHeaders = SplitDelimitedLine(firstLine, delimiter);
        var headers = rawHeaders.Select((h, idx) => string.IsNullOrWhiteSpace(h) ? $"Column_{idx + 1}" : h.Trim()).ToList();

        var rows = new List<Dictionary<string, string>>();
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = SplitDelimitedLine(lines[i], delimiter);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int h = 0; h < headers.Count; h++)
            {
                dict[headers[h]] = h < parts.Count ? parts[h].Trim() : string.Empty;
            }
            if (dict.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
            {
                rows.Add(dict);
            }
        }

        return new ParsedWorksheet(sheetName, headers, rows);
    }

    private static List<string> SplitDelimitedLine(string line, char delimiter)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                result.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString().Trim());
        return result;
    }

    private static int ColumnNameToIndex(string cellRef)
    {
        // Extract letters from A1, B2, AA10 etc.
        int colIndex = 0;
        foreach (char c in cellRef)
        {
            if (char.IsLetter(c))
            {
                colIndex = colIndex * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
            }
            else break;
        }
        return Math.Max(0, colIndex - 1);
    }
}
