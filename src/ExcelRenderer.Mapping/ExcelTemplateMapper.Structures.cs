using System;
using System.Linq;
using System.Threading;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Mapping.Diagnostics;
using ExcelRenderer.Mapping.Templates;
using ExcelRenderer.OpenXml.Reader.Input;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using ExcelRenderer.OpenXml.References;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ExcelRenderer.Mapping;

/// <summary>Validates structural restrictions before evaluating any caller data.</summary>
public static partial class ExcelTemplateMapper
{
    private static void ValidateStructures(PreparedWorkbook source, TemplateSheet[] templates, CancellationToken token, bool allowFormulas = false)
    {
        using var input = source.OpenRead();
        using var document = SpreadsheetDocument.Open(input, false);
        var workbook = document.WorkbookPart!;
        var sheets = workbook.Workbook.Sheets!.Elements<S.Sheet>().ToArray();
        if (templates.Any(template => Repeated(template.Nodes).Any()))
        {
            foreach (var part in workbook.WorksheetParts)
            {
                using var xmlSource = part.GetStream();
                using var xml = System.Xml.XmlReader.Create(xmlSource, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
                while (xml.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (xml.NodeType == System.Xml.XmlNodeType.Element && StructuralReferences.ReadFormulaStore(xml) is { } formula && !StructuralReferences.CanRewrite(formula))
                    {
                        throw new NotSupportedException("Structural formulas with external, structured, array or 3D references cannot move with repeated rows.");
                    }
                }
            }
        }

        foreach (var template in templates)
        {
            token.ThrowIfCancellationRequested();
            var sheet = sheets.Single(item => item.Name!.Value == template.Sheet.Name);
            var part = (WorksheetPart)workbook.GetPartById(sheet.Id!);
            foreach (var node in Repeated(template.Nodes))
            {
                foreach (var name in workbook.Workbook.DefinedNames?.Elements<S.DefinedName>() ?? Enumerable.Empty<S.DefinedName>())
                {
                    if (name.Name!.Value is "_xlnm.Print_Area" or "_xlnm.Print_Titles")
                    {
                        continue;
                    }

                    var owner = name.LocalSheetId is { } scope ? sheets[(int)scope.Value].Name!.Value! : template.Sheet.Name;
                    if (!allowFormulas && StructuralReferences.Rows(name.Text, template.Sheet.Name, owner).Any(range => range.First <= node.LastRow && range.Last >= node.FirstRow))
                    {
                        Fail("Defined name '" + name.Name + "' overlaps a repeated region (" + name.Text + ").");
                    }
                }

                foreach (var pivot in part.PivotTableParts)
                {
                    if (pivot.PivotTableDefinition.Location?.Reference?.Value is { } location && Overlaps(location, node.FirstRow, node.LastRow))
                    {
                        Fail("Pivot output overlapping repeated regions is unsupported.");
                    }
                }

                foreach (var cache in workbook.Parts.Select(pair => pair.OpenXmlPart).OfType<PivotTableCacheDefinitionPart>())
                {
                    var reference = cache.PivotCacheDefinition.CacheSource?.WorksheetSource;
                    if (reference?.Sheet?.Value == template.Sheet.Name && reference.Reference?.Value is { } sourceRange)
                    {
                        var header = CellRange.Parse(sourceRange).First.Row;
                        if (header >= node.FirstRow && header <= node.LastRow)
                        {
                            Fail("Pivot headers overlapping repeated regions are unsupported.");
                        }
                    }
                }

                if (part.TableDefinitionParts.Any(table => Overlaps(table.Table.Reference!, node.FirstRow, node.LastRow)))
                {
                    Fail("Excel tables overlapping repeated regions are not supported; use ordinary cells.");
                }

                if (part.DrawingsPart?.WorksheetDrawing.ChildElements.Any(anchor =>
                {
                    var first = anchor.GetFirstChild<Xdr.FromMarker>()?.RowId?.Text;
                    var last = anchor.GetFirstChild<Xdr.ToMarker>()?.RowId?.Text ?? first;
                    return first != null && int.Parse(first, System.Globalization.CultureInfo.InvariantCulture) + 1 <= node.LastRow && int.Parse(last!, System.Globalization.CultureInfo.InvariantCulture) + 1 >= node.FirstRow;
                }) == true)
                {
                    Fail("Pictures overlapping repeated regions are not supported.");
                }

                void Fail(string message) => throw new MappingException(template.Sheet.Name, node.Location.Address, node.Location.Text, message);
            }
        }
    }

    private static bool Overlaps(string reference, int first, int last)
    {
        var range = CellRange.Parse(reference.Replace("$", string.Empty));
        return range.First.Row <= last && range.Last.Row >= first;
    }
}
