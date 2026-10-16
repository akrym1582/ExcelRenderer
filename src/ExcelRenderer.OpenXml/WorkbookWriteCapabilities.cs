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
        "fileVersion", "fileSharing", "workbookPr", "workbookProtection", "bookViews", "sheets", "definedNames", "calcPr", "pivotCaches",
    };

    private static readonly HashSet<string> StyleChildren = new HashSet<string>(StringComparer.Ordinal)
    {
        "numFmts", "fonts", "fills", "borders", "cellStyleXfs", "cellXfs", "cellStyles", "dxfs", "tableStyles", "colors",
    };

    private static readonly HashSet<string> WorksheetChildren = new HashSet<string>(StringComparer.Ordinal)
    {
        "sheetPr", "dimension", "sheetViews", "sheetFormatPr", "cols", "sheetData", "mergeCells",
        "printOptions", "pageMargins", "pageSetup", "headerFooter", "rowBreaks", "colBreaks", "sheetCalcPr", "phoneticPr", "tableParts", "hyperlinks", "drawing", "legacyDrawing", "autoFilter", "sortState", "conditionalFormatting", "dataValidations", "sheetProtection", "protectedRanges", "ignoredErrors", "pivotTableParts",
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
        WorkbookRelationships.Validate(document, cancellationToken);
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

            if (part is not WorkbookPart and not WorksheetPart and not WorkbookStylesPart and not SharedStringTablePart and not ThemePart and not CoreFilePropertiesPart and not ExtendedFilePropertiesPart and not CustomFilePropertiesPart and not DrawingsPart and not ImagePart and not WorksheetCommentsPart and not VmlDrawingPart and not TableDefinitionPart and not PivotTablePart and not PivotTableCacheDefinitionPart and not PivotTableCacheRecordsPart)
            {
                reasons.Add("UnsupportedPart:" + part.RelationshipType);
            }

            if (part.ExternalRelationships.Any() || part.DataPartReferenceRelationships.Any())
            {
                reasons.Add("ExternalOrDataRelationship");
            }

            foreach (var child in part.Parts)
            {
                pending.Push(child.OpenXmlPart);
            }

            if (part is DrawingsPart or WorksheetCommentsPart or VmlDrawingPart or PivotTableCacheRecordsPart)
            {
                using var source = part.GetStream(FileMode.Open, FileAccess.Read);
                using var xml = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                while (xml.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (xml.NodeType == XmlNodeType.Element && xml.LocalName is "extLst" or "AlternateContent")
                    {
                        reasons.Add("UnsupportedRelatedExtension:" + part.RelationshipType);
                    }

                    if (part is DrawingsPart && xml.NodeType == XmlNodeType.Element && xml.Depth == 1 && xml.LocalName is not "oneCellAnchor" and not "twoCellAnchor" and not "absoluteAnchor")
                    {
                        reasons.Add("UnsupportedDrawingAnchor:" + xml.LocalName);
                    }

                    if (part is VmlDrawingPart && xml.NodeType == XmlNodeType.Element && xml.LocalName == "ClientData" && xml.GetAttribute("ObjectType") != "Note")
                    {
                        reasons.Add("UnsupportedVmlObject");
                    }
                }
            }

            if (part is PivotTablePart or PivotTableCacheDefinitionPart)
            {
                using var source = part.GetStream(FileMode.Open, FileAccess.Read);
                var root = System.Xml.Linq.XElement.Load(source);
                if (root.Descendants().Any(element => element.Name.LocalName is "fieldGroup" or "calculatedItems" or "calculatedMembers" || element.Attribute("formula") != null))
                {
                    reasons.Add("CalculatedOrGroupedPivotCache");
                }

                if (part is PivotTableCacheDefinitionPart && root.Descendants(XmlValues.Spreadsheet + "cacheSource").Any(element => (string?)element.Attribute("type") != "worksheet"))
                {
                    reasons.Add("ExternalPivotCache");
                }

                if (part is PivotTableCacheDefinitionPart cache)
                {
                    var worksheet = root.Descendants(XmlValues.Spreadsheet + "worksheetSource").SingleOrDefault();
                    if (worksheet?.Attribute("sheet") == null || worksheet.Attribute("ref") == null || cache.PivotTableCacheRecordsPart == null)
                    {
                        reasons.Add("UnsupportedPivotSource");
                    }
                }

                foreach (var extension in root.Descendants().Where(element => element.Name.LocalName == "ext"))
                {
                    if ((string?)extension.Attribute("uri") != "{962EF5D1-5CA2-4c93-8EF4-DBF5C05439D2}" || extension.Elements().Any(element => element.Name != System.Xml.Linq.XName.Get("pivotTableDefinition", "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main")))
                    {
                        reasons.Add("UnsupportedPivotExtension");
                    }
                }
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

                    if (xml.LocalName == "f" && xml.NamespaceURI == XmlValues.Spreadsheet.NamespaceName)
                    {
                        reasons.Add("FormulasPresent");
                    }
                }
            }
        }

        input.Position = 0;
        if (WorkbookFormulaInspection.Find(input, cancellationToken) is not null)
        {
            reasons.Add("FormulasOrUninspectableExtensionsPresent");
        }

        input.Position = 0;
        using var reader = OpenXmlWorkbookReader.Open(input, cancellationToken: cancellationToken);
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
