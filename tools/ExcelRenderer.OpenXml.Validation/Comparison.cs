using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ExcelRenderer.OpenXml.Reading;

/// <summary>Compares reader projections with immutable parent-revision results.</summary>
internal static class Comparison
{
    /// <summary>Compares every repository sample with its retained source projection.</summary>
    /// <param name="root">Repository root.</param>
    /// <param name="output">Evidence JSON destination.</param>
    /// <returns>Zero when every projection matches.</returns>
    internal static int Run(string root, string output)
    {
        var baselinePath = Path.Combine(root, "docs", "validation", "closedxml-removal", "raw-reader-baseline.json");
        using var baseline = JsonDocument.Parse(File.ReadAllText(baselinePath));
        var results = new List<object>();
        var failures = 0;
        foreach (var item in baseline.RootElement.GetProperty("Files").EnumerateArray())
        {
            var relative = item.GetProperty("File").GetString()!;
            var path = Path.Combine(root, relative);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            var actual = JsonSerializer.SerializeToElement(Project(path));
            var match = hash == item.GetProperty("Sha256").GetString() && actual.GetRawText() == item.GetProperty("Projection").GetRawText();
            if (!match)
            {
                failures++;
            }

            results.Add(new { File = relative, Sha256 = hash, Match = match, Actual = match ? (JsonElement?)null : actual });
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Baseline = baselinePath, UnexplainedDifferences = failures, Files = results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Captures the complete source-neutral values, styles and worksheet metadata.</summary>
    /// <param name="path">Source workbook.</param>
    /// <returns>A deterministic projection without live package objects.</returns>
    internal static object Project(string path)
    {
        using var input = File.OpenRead(path);
        using var reader = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { Culture = CultureInfo.GetCultureInfo("en-US") });
        return new
        {
            reader.Workbook,
            Sheets = reader.Sheets.Select(sheet => new
            {
                Info = reader.ReadWorksheetInfo(sheet.Index),
                Cells = reader.EnumerateCells(sheet.Index).Select(cell => new { Cell = cell, Style = reader.ResolveStyle(cell), Display = reader.FormatCell(cell) }).ToArray(),
            }).ToArray(),
        };
    }
}
