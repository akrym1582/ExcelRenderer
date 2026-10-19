using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using ExcelRenderer.OpenXml.Reader.Diagnostics;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using ExcelRenderer.OpenXml.Reader.Model.Printing;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;

namespace ExcelRenderer.OpenXml.Reader.References;

/// <summary>Provides PrintReferences metadata.</summary>
internal static class PrintReferences
{
    /// <summary>Provides Parse metadata.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="name">The name value.</param>
    /// <param name="sheet">The sheet value.</param>
    /// <param name="sink">The sink value.</param>
    internal static PrintReferenceInfo Parse(DefinedNameInfo name, SheetInfo sheet, Action<ReaderDiagnostic>? sink)
    {
        var ranges = new List<PrintRangeInfo>();
        try
        {
            foreach (var item in Split(name.RawText))
            {
                var token = item.Trim();
                var bang = token.LastIndexOf('!');
                if (bang >= 0)
                {
                    var qualifier = token.Substring(0, bang);
                    if (qualifier.StartsWith("'", StringComparison.Ordinal) && qualifier.EndsWith("'", StringComparison.Ordinal))
                    {
                        qualifier = qualifier.Substring(1, qualifier.Length - 2).Replace("''", "'");
                    }

                    if (!string.Equals(qualifier, sheet.Name, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException();
                    }

                    token = token.Substring(bang + 1);
                }

                if (Regex.IsMatch(token, @"^\$?[1-9][0-9]*:\$?[1-9][0-9]*$"))
                {
                    var ends = token.Replace("$", string.Empty).Split(':');
                    var first = int.Parse(ends[0], CultureInfo.InvariantCulture);
                    var last = int.Parse(ends[1], CultureInfo.InvariantCulture);
                    if (first > last || last > 1048576)
                    {
                        throw new InvalidDataException();
                    }

                    ranges.Add(new PrintRangeInfo("Rows", first, last, 1, 16384));
                }
                else if (Regex.IsMatch(token, @"^\$?[A-Z]{1,3}:\$?[A-Z]{1,3}$"))
                {
                    var ends = token.Replace("$", string.Empty).Split(':');
                    var first = CellAddress.Parse(ends[0] + "1").Column;
                    var last = CellAddress.Parse(ends[1] + "1").Column;
                    if (first > last)
                    {
                        throw new InvalidDataException();
                    }

                    ranges.Add(new PrintRangeInfo("Columns", 1, 1048576, first, last));
                }
                else
                {
                    var range = CellRange.Parse(token);
                    ranges.Add(new PrintRangeInfo("Cells", range.First.Row, range.Last.Row, range.First.Column, range.Last.Column));
                }
            }
        }
        catch (Exception e) when (e is InvalidDataException || e is FormatException || e is OverflowException)
        {
            ranges.Clear();
            sink?.Invoke(new ReaderDiagnostic(ReaderDiagnosticCodes.UnsupportedPrintReference, "Warning", sheet.Index, null, "Print definition is outside the supported A1 union grammar; original text is retained."));
        }

        return new PrintReferenceInfo(name.Name, name.RawText, ranges.AsReadOnly());
    }

    /// <summary>Splits an A1 union outside quoted worksheet names.</summary>
    /// <param name="text">Saved reference union.</param>
    /// <returns>The unchanged reference tokens.</returns>
    internal static IEnumerable<string> Split(string text)
    {
        var quoted = false;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\'')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (text[i] == ',' && !quoted)
            {
                yield return text.Substring(start, i - start);
                start = i + 1;
            }
        }

        if (quoted)
        {
            throw new InvalidDataException();
        }

        yield return text.Substring(start);
    }
}
