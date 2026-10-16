using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.OpenXml.Input;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.OpenXml;

/// <summary>Updates workbook relationships and structural references from the same row plan.</summary>
public static partial class OpenXmlWorkbookWriter
{
    private static void RewriteStructures(Stream input, ZipArchive archive, Dictionary<string, SheetSource> sources, Dictionary<string, SpillableBufferStream> replacements, StructuralReferences references, InputBufferOptions buffers, CancellationToken token)
    {
        input.Position = 0;
        using var document = SpreadsheetDocument.Open(new LeaveOpenStream(input), false);
        var workbook = document.WorkbookPart!;
        var sheetNames = workbook.Workbook.Sheets!.Elements<S.Sheet>().Select(sheet => sheet.Name!.Value!).ToArray();
        foreach (var definition in workbook.Workbook.DefinedNames?.Elements<S.DefinedName>() ?? Enumerable.Empty<S.DefinedName>())
        {
            if (definition.Name!.Value is not "_xlnm.Print_Area" and not "_xlnm.Print_Titles")
            {
                references.ValidateName(definition.Text, sheetNames[(int?)definition.LocalSheetId?.Value ?? 0]);
            }
        }

        if (sources.Values.Any(source => source.Plan.Rows.Count != source.Plan.SourceLastRow || source.Plan.Rows.Any(row => row.SourceRow != row.OutputRow)))
        {
            foreach (var formula in sources.Values.SelectMany(source => source.Children).SelectMany(element => element.DescendantsAndSelf()).Where(element => element.Name.LocalName is "formula" or "formula1" or "formula2"))
            {
                if (!StructuralReferences.CanRewrite(formula.Value))
                {
                    throw new NotSupportedException("A structural formula uses unsupported reference syntax.");
                }
            }
        }

        RewritePivotCaches(workbook, archive, sources, replacements, references, buffers, token);
        foreach (var sheet in workbook.Workbook.Sheets!.Elements<S.Sheet>())
        {
            token.ThrowIfCancellationRequested();
            var part = (WorksheetPart)workbook.GetPartById(sheet.Id!);
            var source = sources[part.Uri.ToString().TrimStart('/')];
            var name = sheet.Name!.Value!;
            RewriteSheetStructures(source, references, name);
            foreach (var pivot in part.PivotTableParts)
            {
                EditPart(pivot, root =>
                {
                    foreach (var location in root.Elements(Ns + "location"))
                    {
                        var range = CellRange.Parse((string)location.Attribute("ref")!);
                        RequireUniqueRows(source.Plan, range.First.Row, range.Last.Row, "A pivot output intersects repeated or deleted rows.");
                    }

                    RewriteAttributes(root, source.Plan, references, name);
                    var cache = pivot.PivotTableCacheDefinitionPart;
                    if (cache != null && replacements.TryGetValue(cache.Uri.ToString().TrimStart('/'), out var cacheStream))
                    {
                        cacheStream.Position = 0;
                        var fields = XDocument.Load(cacheStream).Root!.Element(Ns + "cacheFields")!.Elements().ToArray();
                        foreach (var field in root.Element(Ns + "pivotFields")?.Elements().Select((element, index) => (Element: element, Index: index)) ?? Enumerable.Empty<(XElement Element, int Index)>())
                        {
                            if (field.Element.Element(Ns + "items") is { } items)
                            {
                                var totals = items.Elements().Where(item => item.Attribute("x") == null).Select(item => new XElement(item)).ToArray();
                                var count = fields[field.Index].Element(Ns + "sharedItems")!.Elements().Count();
                                items.ReplaceNodes(Enumerable.Range(0, count).Select(index => new XElement(Ns + "item", new XAttribute("x", index))), totals);
                                items.SetAttributeValue("count", items.Elements().Count());
                            }
                        }
                    }
                });
            }

            foreach (var table in part.TableDefinitionParts)
            {
                EditPart(table, root =>
                {
                    var range = CellRange.Parse((string)root.Attribute("ref")!);
                    RequireUniqueRows(source.Plan, range.First.Row, range.Last.Row, "A table intersects repeated or deleted rows.");
                    RewriteAttributes(root, source.Plan, references, name);
                });
            }

            if (part.DrawingsPart is { } drawing)
            {
                EditPart(drawing, root =>
                {
                    foreach (var anchor in root.Elements())
                    {
                        var rows = anchor.Descendants().Where(element => element.Name.LocalName == "row").Select(element => int.Parse(element.Value, CultureInfo.InvariantCulture) + 1).ToArray();
                        if (rows.Length > 0)
                        {
                            RequireUniqueRows(source.Plan, rows.Min(), rows.Max(), "A drawing intersects repeated or deleted rows.");
                        }
                    }

                    foreach (var row in root.Descendants().Where(element => element.Name.LocalName == "row"))
                    {
                        var mapped = references.FirstRow(source.Plan, int.Parse(row.Value, CultureInfo.InvariantCulture) + 1);
                        row.Value = ((mapped ?? throw new NotSupportedException("A drawing anchor was deleted.")) - 1).ToString(CultureInfo.InvariantCulture);
                    }
                });
            }

            if (part.WorksheetCommentsPart is { } comments)
            {
                EditPart(comments, root =>
                {
                    foreach (var comment in root.Descendants(Ns + "comment").ToArray())
                    {
                        var expanded = StructuralReferences.Expand((string)comment.Attribute("ref")!, source.Plan);
                        comment.ReplaceWith(expanded.Select(item =>
                        {
                            var clone = new XElement(comment);
                            clone.SetAttributeValue("ref", item.Reference);
                            return clone;
                        }));
                    }
                });
            }

            foreach (var vml in part.VmlDrawingParts)
            {
                EditPart(vml, root =>
                {
                    var nextId = root.Descendants().Where(element => element.Name.LocalName == "shape").Select(element => (string?)element.Attribute("id")).Select(id => int.TryParse(System.Text.RegularExpressions.Regex.Match(id ?? string.Empty, "[0-9]+$").Value, out var number) ? number : 1024).DefaultIfEmpty(1024).Max();
                    foreach (var shape in root.Descendants().Where(element => element.Name.LocalName == "shape").ToArray())
                    {
                        var row = shape.Descendants().FirstOrDefault(element => element.Name.LocalName == "Row");
                        if (row == null)
                        {
                            throw new NotSupportedException("Only cell-comment VML shapes can be structurally edited.");
                        }

                        var originalRow = int.Parse(row.Value, CultureInfo.InvariantCulture) + 1;
                        var occurrences = StructuralReferences.Expand("A" + originalRow.ToString(CultureInfo.InvariantCulture), source.Plan);
                        shape.ReplaceWith(occurrences.Select((item, index) =>
                        {
                            var clone = new XElement(shape);
                            if (index > 0)
                            {
                                clone.SetAttributeValue("id", "_x0000_s" + (++nextId).ToString(CultureInfo.InvariantCulture));
                            }

                            clone.Descendants().Single(element => element.Name.LocalName == "Row").Value = (item.OutputRow - 1).ToString(CultureInfo.InvariantCulture);
                            var anchor = clone.Descendants().FirstOrDefault(element => element.Name.LocalName == "Anchor");
                            if (anchor != null)
                            {
                                var values = anchor.Value.Split(',').Select(value => int.Parse(value.Trim(), CultureInfo.InvariantCulture)).ToArray();
                                if (values.Length != 8)
                                {
                                    throw new InvalidDataException("Invalid VML comment anchor.");
                                }

                                values[2] = checked(values[2] + item.OutputRow - originalRow);
                                values[6] = checked(values[6] + item.OutputRow - originalRow);
                                anchor.Value = string.Join(", ", values);
                            }

                            return clone;
                        }).ToArray());
                    }
                });
            }
        }

        void EditPart(OpenXmlPart part, Action<XElement> edit)
        {
            token.ThrowIfCancellationRequested();
            var path = part.Uri.ToString().TrimStart('/');
            using var original = archive.GetEntry(path)!.Open();
            var xml = XDocument.Load(original);
            var before = xml.ToString(SaveOptions.DisableFormatting);
            edit(xml.Root!);
            if (before != xml.ToString(SaveOptions.DisableFormatting))
            {
                var replacement = new SpillableBufferStream(buffers);
                replacements.Add(path, replacement);
                xml.Save(replacement);
            }
        }
    }

    private static void RewriteSheetStructures(SheetSource source, StructuralReferences references, string name)
    {
        for (var index = 0; index < source.Children.Count; index++)
        {
            var element = source.Children[index];
            if (element.Name.LocalName == "conditionalFormatting")
            {
                var clones = StructuralReferences.Expand((string)element.Attribute("sqref")!, source.Plan).Select(item =>
                {
                    var clone = new XElement(element);
                    clone.SetAttributeValue("sqref", item.Reference);
                    var delta = item.OutputRow - (references.FirstRow(source.Plan, item.SourceRow) ?? item.OutputRow);
                    foreach (var formula in clone.Descendants(Ns + "formula"))
                    {
                        formula.Value = references.Rewrite(formula.Value, name, delta);
                    }

                    return clone;
                }).ToArray();
                source.Children.RemoveAt(index);
                source.Children.InsertRange(index, clones);
                index += clones.Length - 1;
                continue;
            }

            if (element.Name.LocalName is "hyperlinks" or "dataValidations")
            {
                foreach (var child in element.Elements().ToArray())
                {
                    var attribute = child.Name.LocalName == "hyperlink" ? "ref" : "sqref";
                    child.ReplaceWith(StructuralReferences.Expand((string)child.Attribute(attribute)!, source.Plan).Select(item =>
                    {
                        var clone = new XElement(child);
                        clone.SetAttributeValue(attribute, item.Reference);
                        if (clone.Attribute("location") is { } location)
                        {
                            location.Value = references.Rewrite(location.Value, name);
                        }

                        var delta = item.OutputRow - (references.FirstRow(source.Plan, item.SourceRow) ?? item.OutputRow);
                        foreach (var formula in clone.Elements().Where(value => value.Name.LocalName is "formula1" or "formula2"))
                        {
                            formula.Value = references.Rewrite(formula.Value, name, delta);
                        }

                        return clone;
                    }).ToArray());
                }

                if (element.Name.LocalName == "dataValidations")
                {
                    element.SetAttributeValue("count", element.Elements().Count());
                }

                if (!element.HasElements)
                {
                    source.Children.RemoveAt(index--);
                }
            }
            else if (element.Name.LocalName is "autoFilter" or "sortState" or "sheetViews" or "ignoredErrors" or "protectedRanges")
            {
                if (element.Name.LocalName is "autoFilter" or "sortState" && element.Attribute("ref") is { } reference && references.Rewrite(reference.Value, name) == "#REF!")
                {
                    source.Children.RemoveAt(index--);
                }
                else
                {
                    RewriteAttributes(element, source.Plan, references, name);
                }
            }
        }
    }

    private static void RewriteAttributes(XElement root, SheetEditPlan plan, StructuralReferences references, string owner)
    {
        foreach (var attribute in root.DescendantsAndSelf().Attributes().Where(attribute => attribute.Name.LocalName is "ref" or "sqref" or "activeCell" or "topLeftCell").ToArray())
        {
            if (attribute.Name.LocalName == "sqref")
            {
                var ranges = StructuralReferences.Expand(attribute.Value, plan);
                attribute.Value = string.Join(" ", ranges.Select(item => item.Reference));
                if (attribute.Value.Length == 0)
                {
                    if (attribute.Parent?.Parent != null)
                    {
                        attribute.Parent.Remove();
                    }
                    else
                    {
                        attribute.Remove();
                    }
                }
            }
            else
            {
                attribute.Value = references.Rewrite(attribute.Value, owner);
                if (attribute.Value == "#REF!")
                {
                    if (attribute.Name.LocalName is "activeCell" or "topLeftCell")
                    {
                        attribute.Value = "A1";
                        continue;
                    }

                    if (attribute.Parent?.Parent != null)
                    {
                        attribute.Parent.Remove();
                    }
                    else
                    {
                        attribute.Remove();
                    }
                }
            }
        }
    }

    private static void RequireUniqueRows(SheetEditPlan plan, int first, int last, string message)
    {
        var rows = plan.Rows.Where(row => row.SourceRow >= first && row.SourceRow <= last).ToArray();
        var expected = Math.Max(0, Math.Min(last, plan.SourceLastRow) - first + 1);
        if (rows.Length != expected || rows.Select(row => row.SourceRow).Distinct().Count() != expected)
        {
            throw new NotSupportedException(message);
        }
    }

    private static void RewriteDefinedNames(XDocument workbook, StructuralReferences references)
    {
        var names = workbook.Root!.Element(Ns + "sheets")!.Elements().Select(sheet => (string)sheet.Attribute("name")!).ToArray();
        foreach (var definition in workbook.Root.Element(Ns + "definedNames")?.Elements().ToArray() ?? Enumerable.Empty<XElement>())
        {
            var scope = (int?)definition.Attribute("localSheetId") ?? 0;
            var name = (string)definition.Attribute("name")!;
            if (name is "_xlnm.Print_Area" or "_xlnm.Print_Titles")
            {
                var values = new List<string>();
                foreach (var item in PrintReferences.Split(definition.Value))
                {
                    if (name == "_xlnm.Print_Titles" && !System.Text.RegularExpressions.Regex.IsMatch(item, "(?:!|^)\\$?[A-Za-z]+:\\$?[A-Za-z]+$"))
                    {
                        references.ValidateName(item, names[scope]);
                    }

                    var updated = references.Rewrite(item, names[scope], expandSingleCell: name == "_xlnm.Print_Area");
                    if (updated != "#REF!")
                    {
                        values.Add(updated);
                    }
                }

                if (values.Count == 0)
                {
                    definition.Remove();
                }
                else
                {
                    definition.Value = string.Join(",", values);
                }
            }
            else
            {
                definition.Value = references.Rewrite(definition.Value, names[scope]);
            }
        }
    }
}
