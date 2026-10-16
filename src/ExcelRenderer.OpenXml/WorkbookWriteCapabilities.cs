using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace ExcelRenderer.OpenXml;

/// <summary>Checks the conservative, formula-free workbook editing contract without evaluating mapping data.</summary>
public static class WorkbookWriteCapabilities
{
    private static readonly HashSet<string> WorkbookChildren = new HashSet<string>(StringComparer.Ordinal)
    {
        "fileVersion", "fileSharing", "workbookPr", "workbookProtection", "bookViews", "sheets", "definedNames", "calcPr",
    };

    private static readonly HashSet<string> StyleChildren = new HashSet<string>(StringComparer.Ordinal)
    {
        "numFmts", "fonts", "fills", "borders", "cellStyleXfs", "cellXfs", "cellStyles", "dxfs", "tableStyles", "colors",
    };

    private static readonly HashSet<string> WorksheetChildren = new HashSet<string>(StringComparer.Ordinal)
    {
        "sheetPr", "dimension", "sheetViews", "sheetFormatPr", "cols", "sheetData", "mergeCells",
        "printOptions", "pageMargins", "pageSetup", "headerFooter", "rowBreaks", "colBreaks", "sheetCalcPr", "phoneticPr", "tableParts",
    };

    /// <summary>Inspects every sheet and relationship before selecting the editing engine.</summary>
    /// <param name="input">Validated seekable input at position zero; left open.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Every distinct unsupported feature reason, or an empty list.</returns>
    public static IReadOnlyList<string> Inspect(Stream input, CancellationToken cancellationToken = default)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        using var document = SpreadsheetDocument.Open(new LeaveOpenStream(input), false);
        if (document.DocumentType != SpreadsheetDocumentType.Workbook)
        {
            reasons.Add("UnsupportedPackageType");
        }

        var pending = new Stack<OpenXmlPart>(document.Parts.Select(pair => pair.OpenXmlPart));
        var visited = new HashSet<Uri>();
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var part = pending.Pop();
            if (!visited.Add(part.Uri))
            {
                continue;
            }

            if (part is not WorkbookPart and not WorksheetPart and not WorkbookStylesPart and not SharedStringTablePart and not ThemePart and not CoreFilePropertiesPart and not ExtendedFilePropertiesPart and not CustomFilePropertiesPart)
            {
                reasons.Add("UnsupportedPart:" + part.RelationshipType);
            }

            if (part.ExternalRelationships.Any() || part.HyperlinkRelationships.Any() || part.DataPartReferenceRelationships.Any())
            {
                reasons.Add("ExternalOrDataRelationship");
            }

            foreach (var child in part.Parts)
            {
                pending.Push(child.OpenXmlPart);
            }

            if (part is WorksheetPart or WorkbookPart or WorkbookStylesPart)
            {
                var children = part is WorksheetPart ? WorksheetChildren : part is WorkbookPart ? WorkbookChildren : StyleChildren;
                var kind = part is WorksheetPart ? "Worksheet" : part is WorkbookPart ? "Workbook" : "Styles";
                using var source = part.GetStream(FileMode.Open, FileAccess.Read);
                using var xml = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                while (xml.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (xml.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    if (xml.Depth == 0 && xml.NamespaceURI != XmlValues.Spreadsheet.NamespaceName)
                    {
                        reasons.Add("Unsupported" + kind + "Namespace");
                    }

                    if (xml.Depth == 1 && (!children.Contains(xml.LocalName) || xml.NamespaceURI != XmlValues.Spreadsheet.NamespaceName))
                    {
                        reasons.Add("Unsupported" + kind + "Child:" + xml.LocalName);
                    }

                    if (xml.LocalName is "extLst" or "AlternateContent")
                    {
                        reasons.Add("UnsupportedExtension:" + xml.LocalName);
                    }

                    if (xml.LocalName == "tablePart")
                    {
                        reasons.Add("TablesPresent");
                    }

                    if (xml.LocalName == "f" && xml.NamespaceURI == XmlValues.Spreadsheet.NamespaceName)
                    {
                        reasons.Add("FormulasPresent");
                    }
                }
            }
        }

        input.Position = 0;
        using var reader = OpenXmlWorkbookReader.Open(input, cancellationToken: cancellationToken);
        if (reader.Workbook.DefinedNames.Any(name => name.Name is not "_xlnm.Print_Area" and not "_xlnm.Print_Titles"))
        {
            reasons.Add("GeneralDefinedNames");
        }

        if (reader.Workbook.DefinedNames.Any(name => name.Name is "_xlnm.Print_Area" or "_xlnm.Print_Titles" && name.LocalSheetId == null))
        {
            reasons.Add("UnscopedPrintDefinition");
        }

        foreach (var sheet in reader.Sheets)
        {
            if (sheet.Kind != "Worksheet")
            {
                reasons.Add("UnsupportedSheetKind:" + sheet.Kind);
                continue;
            }

            var info = reader.ReadWorksheetInfo(sheet.Index, cancellationToken);
            if (info.PrintReferences.Any(reference => reference.Ranges.Count == 0))
            {
                reasons.Add("UnsupportedPrintReference");
            }

            foreach (var style in info.Rows.Where(row => row.StyleIndex != null).Select(row => row.StyleIndex!.Value)
                .Concat(info.Columns.Where(column => column.StyleIndex != null).Select(column => column.StyleIndex!.Value)).Distinct())
            {
                reader.ResolveStyle(style);
            }

            // Invalid input is fatal even when another capability is unsupported.
            foreach (var cell in reader.EnumerateCells(sheet.Index, cancellationToken))
            {
                reader.ResolveStyle(cell);
                if (cell.Formula != null)
                {
                    reasons.Add("FormulasPresent");
                }
            }
        }

        return Array.AsReadOnly(reasons.OrderBy(reason => reason, StringComparer.Ordinal).ToArray());
    }
}
