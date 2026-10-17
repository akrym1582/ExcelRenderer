using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.OpenXml.Infrastructure;
using ExcelRenderer.OpenXml.Model.Cells;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.OpenXml.Calculation;

/// <summary>Finds value-producing formulas before any Mapping data is evaluated.</summary>
internal static class WorkbookFormulaInspection
{
    private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly Regex Constant = new Regex("\\A(?:\"(?:[^\"]|\"\")*\"|[+-]?(?:[0-9]+(?:\\.[0-9]*)?|\\.[0-9]+)(?:[Ee][+-]?[0-9]+)?|TRUE|FALSE)\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Reference = new Regex(@"\A(?:\$?[A-Za-z]{1,3}\$?[1-9][0-9]{0,6}(?::\$?[A-Za-z]{1,3}\$?[1-9][0-9]{0,6})?|\$?[A-Za-z]{1,3}:\$?[A-Za-z]{1,3}|\$?[1-9][0-9]{0,6}:\$?[1-9][0-9]{0,6})\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Finds the first prohibited formula or uninspectable worksheet extension.</summary>
    /// <param name="input">Validated seekable XLSX input, left open.</param>
    /// <param name="token">Cooperative cancellation.</param>
    /// <returns>The source location, or null when all value definitions are static.</returns>
    internal static FormulaLocation? Find(Stream input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var document = SpreadsheetDocument.Open(new LeaveOpenStream(input), false);
        var workbook = document.WorkbookPart!;
        if (workbook.Workbook.ChildElements.Any(element => element.LocalName is "extLst" or "AlternateContent"))
        {
            return new FormulaLocation(string.Empty, string.Empty, string.Empty, "UnknownWorkbookExtension");
        }

        var sheetNames = workbook.Workbook.Sheets!.Elements<S.Sheet>().ToArray();
        var names = sheetNames.Select(sheet => sheet.Name!.Value!).ToArray();
        foreach (var name in workbook.Workbook.DefinedNames?.Elements<S.DefinedName>() ?? Enumerable.Empty<S.DefinedName>())
        {
            token.ThrowIfCancellationRequested();
            if (!IsStaticDefinition(name.Text, names))
            {
                var sheet = name.LocalSheetId?.Value is { } index && index < names.Length ? names[index] : string.Empty;
                return new FormulaLocation(sheet, string.Empty, name.Name?.Value ?? string.Empty, "DefinedName");
            }
        }

        // Walk every related part, including tables not selected by a rendering request.
        var pending = new Stack<OpenXmlPart>(document.Parts.Select(pair => pair.OpenXmlPart));
        var visited = new HashSet<Uri>();
        var owners = new Dictionary<Uri, string>();
        foreach (var sheet in sheetNames)
        {
            owners[workbook.GetPartById(sheet.Id!).Uri] = sheet.Name!.Value!;
        }

        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var part = pending.Pop();
            if (!visited.Add(part.Uri))
            {
                continue;
            }

            var sheet = owners.GetValueOrDefault(part.Uri, string.Empty);
            foreach (var child in part.Parts)
            {
                if (!owners.ContainsKey(child.OpenXmlPart.Uri))
                {
                    owners[child.OpenXmlPart.Uri] = sheet;
                }

                pending.Push(child.OpenXmlPart);
            }

            if (part is not WorksheetPart and not TableDefinitionPart)
            {
                continue;
            }

            using var source = part.GetStream(FileMode.Open, FileAccess.Read);
            using var xml = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var cell = string.Empty;
            var table = string.Empty;
            while (xml.Read())
            {
                token.ThrowIfCancellationRequested();
                if (xml.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (xml.NamespaceURI != MainNamespace)
                {
                    return new FormulaLocation(sheet, string.Empty, table, "UnknownNamespace");
                }

                if (xml.LocalName == "c" && xml.NamespaceURI == MainNamespace)
                {
                    cell = xml.GetAttribute("r") ?? string.Empty;
                }
                else if (xml.LocalName == "table")
                {
                    table = xml.GetAttribute("displayName") ?? xml.GetAttribute("name") ?? string.Empty;
                }

                if (xml.LocalName == "tableColumn" && xml.GetAttribute("totalsRowFunction") is { } function && function != "none")
                {
                    return new FormulaLocation(sheet, string.Empty, table, "TotalsRowFunction");
                }

                if ((xml.LocalName == "f") ||
                    xml.LocalName is "calculatedColumnFormula" or "totalsRowFormula" or "extLst" or "AlternateContent")
                {
                    // Unknown extension payloads cannot establish that value formulas are absent.
                    // Conditional-formatting and validation formulas use formula/formula1/formula2.
                    return new FormulaLocation(sheet, part is WorksheetPart && xml.LocalName == "f" ? cell : string.Empty, table, xml.LocalName);
                }
            }
        }

        return null;
    }

    /// <summary>Accepts only literal constants and finite unions of static A1 references.</summary>
    /// <param name="text">Original defined-name text.</param>
    /// <param name="sheets">Known worksheet names.</param>
    /// <returns>Whether the definition requires no calculation.</returns>
    internal static bool IsStaticDefinition(string text, IReadOnlyList<string> sheets)
    {
        text = text.Trim();
        if (text.StartsWith("=", StringComparison.Ordinal))
        {
            text = text.Substring(1).Trim();
        }

        if (Constant.IsMatch(text))
        {
            return true;
        }

        // Split unions outside quoted sheet names, including escaped apostrophes.
        var quoted = false;
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] == '\'')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                quoted = !quoted;
            }

            if (i < text.Length && (text[i] != ',' || quoted))
            {
                continue;
            }

            var reference = text.Substring(start, i - start).Trim();
            var bang = reference.LastIndexOf('!');
            if (bang >= 0)
            {
                var name = reference.Substring(0, bang);
                if (name.StartsWith("'", StringComparison.Ordinal) && name.EndsWith("'", StringComparison.Ordinal) && name.Length >= 2)
                {
                    name = name.Substring(1, name.Length - 2).Replace("''", "'");
                }
                else if (!Regex.IsMatch(name, @"\A[\p{L}\p{N}_.]+\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                {
                    return false;
                }

                if (!sheets.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }

                reference = reference.Substring(bang + 1);
            }

            if (!Reference.IsMatch(reference) || !InGrid(reference))
            {
                return false;
            }

            start = i + 1;
        }

        return !quoted && text.Length > 0;
    }

    private static bool InGrid(string reference)
    {
        foreach (var endpoint in reference.Replace("$", string.Empty).Split(':'))
        {
            if (int.TryParse(endpoint, out var row))
            {
                if (row > 1048576)
                {
                    return false;
                }
            }
            else
            {
                var address = char.IsLetter(endpoint[endpoint.Length - 1]) ? endpoint + "1" : endpoint;
                try
                {
                    CellAddress.Parse(address.ToUpperInvariant());
                }
                catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or InvalidDataException)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Identifies a prohibited source expression without depending on Mapping.</summary>
    internal sealed class FormulaLocation
    {
        /// <summary>Initializes a new instance of the <see cref="FormulaLocation"/> class.</summary>
        /// <param name="sheet">Original sheet name when available.</param>
        /// <param name="cell">Original cell address when available.</param>
        /// <param name="name">Defined name or table name.</param>
        /// <param name="kind">Source expression kind.</param>
        internal FormulaLocation(string sheet, string cell, string name, string kind)
        {
            Sheet = sheet;
            Cell = cell;
            Name = name;
            Kind = kind;
        }

        /// <summary>Gets the original sheet location.</summary>
        internal string Sheet { get; }

        /// <summary>Gets the original cell location.</summary>
        internal string Cell { get; }

        /// <summary>Gets the original name location.</summary>
        internal string Name { get; }

        /// <summary>Gets the original kind location.</summary>
        internal string Kind { get; }
    }
}
