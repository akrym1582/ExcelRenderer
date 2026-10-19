using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.OpenXml.Reader.Infrastructure;
using ExcelRenderer.OpenXml.Reader.Input;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using ExcelRenderer.OpenXml.Reader.References;
using ExcelRenderer.OpenXml.References;
using ExcelRenderer.OpenXml.Writing.Plans;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.OpenXml.Writing;

/// <summary>Updates workbook relationships and structural references from the same row plan.</summary>
public static partial class OpenXmlWorkbookWriter
{
    private static void RewriteStructures(Stream input, ZipArchive archive, Dictionary<string, SheetSource> sources, Dictionary<string, SpillableBufferStream> replacements, StructuralReferences references, InputBufferOptions buffers, CancellationToken token, bool formulaAst = false)
    {
        input.Position = 0;
        using var document = SpreadsheetDocument.Open(new LeaveOpenStream(input), false);
        var workbook = document.WorkbookPart!;
        var sheetNames = workbook.Workbook.Sheets!.Elements<S.Sheet>().Select(sheet => sheet.Name!.Value!).ToArray();
        foreach (var definition in workbook.Workbook.DefinedNames?.Elements<S.DefinedName>() ?? Enumerable.Empty<S.DefinedName>())
        {
            if (!formulaAst && definition.Name!.Value is not "_xlnm.Print_Area" and not "_xlnm.Print_Titles")
            {
                references.ValidateName(definition.Text, sheetNames[(int?)definition.LocalSheetId?.Value ?? 0]);
            }
        }

        if (sources.Values.Any(source => source.Plan.Rows.Count != source.Plan.SourceLastRow || source.Plan.Rows.Any(row => row.SourceRow != row.OutputRow)))
        {
            foreach (var formula in sources.Values.SelectMany(source => source.Children).SelectMany(StructuralReferences.FormulaStores))
            {
                if (!StructuralReferences.CanRewrite(StructuralReferences.FormulaText(formula)))
                {
                    throw new NotSupportedException("A structural formula uses unsupported reference syntax.");
                }
            }
        }

        var cacheUpdates = RewritePivotCaches(workbook, archive, sources, replacements, references, buffers, token);
        foreach (var sheet in workbook.Workbook.Sheets!.Elements<S.Sheet>())
        {
            token.ThrowIfCancellationRequested();
            var part = (WorksheetPart)workbook.GetPartById(sheet.Id!);
            var source = sources[part.Uri.ToString().TrimStart('/')];
            var name = sheet.Name!.Value!;
            RewriteSheetStructures(source, references, name, token);
            foreach (var pivot in part.PivotTableParts)
            {
                EditPart(pivot, root =>
                {
                    foreach (var location in root.Elements(Ns + "location"))
                    {
                        var range = CellRange.Parse((string)location.Attribute("ref")!);
                        RequireUniqueRows(source.Plan, range.First.Row, range.Last.Row, "A pivot output intersects repeated or deleted rows.");
                        RewriteAttributes(location, source.Plan, references, name);
                    }

                    var cache = pivot.PivotTableCacheDefinitionPart;
                    if (cache != null && cacheUpdates.TryGetValue(cache.Uri, out var update) && update.Kind == PivotCacheChange.ItemsChanged)
                    {
                        foreach (var field in root.Element(Ns + "pivotFields")?.Elements().Select((element, index) => (Element: element, Index: index)) ?? Enumerable.Empty<(XElement Element, int Index)>())
                        {
                            if (field.Element.Element(Ns + "items") is { } items && update.AddedItems[field.Index].Length > 0)
                            {
                                // Append after existing items (including subtotals). Their positions
                                // are referenced by rowItems, colItems, pageFields and pivot areas.
                                items.Add(update.AddedItems[field.Index].Select(index => new XElement(Ns + "item", new XAttribute("x", index))));
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

    private static void RewriteSheetStructures(SheetSource source, StructuralReferences references, string name, CancellationToken token)
    {
        for (var index = 0; index < source.Children.Count; index++)
        {
            var element = source.Children[index];
            if (element.Name.LocalName == "conditionalFormatting")
            {
                var rules = element.Elements(Ns + "cfRule").ToArray();
                var clones = references.HasRowChanges && rules.Length > 1 ?
                    rules.SelectMany(rule => RewriteRule(new XElement(element.Name, element.Attributes(), new XElement(rule)), source.Plan, references, name, token, RequiresPopulation(rule))).ToArray() :
                    RewriteRule(element, source.Plan, references, name, token, rules.Any(RequiresPopulation));
                source.Children.RemoveAt(index);
                source.Children.InsertRange(index, clones);
                index += clones.Count - 1;
                continue;
            }

            if (element.Name.LocalName is "hyperlinks" or "dataValidations")
            {
                foreach (var child in element.Elements().ToArray())
                {
                    if (element.Name.LocalName == "dataValidations")
                    {
                        child.ReplaceWith(RewriteRule(child, source.Plan, references, name, token, preservePopulation: false));
                    }
                    else
                    {
                        child.ReplaceWith(StructuralReferences.Expand((string)child.Attribute("ref")!, source.Plan).Select(item =>
                        {
                            var clone = new XElement(child);
                            clone.SetAttributeValue("ref", item.Reference);
                            if (clone.Attribute("location") is { } location)
                            {
                                location.Value = references.Rewrite(location.Value, name);
                            }

                            return clone;
                        }).ToArray());
                    }
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

    private static bool RequiresPopulation(XElement rule) => (string?)rule.Attribute("type") is "colorScale" or "dataBar" or "iconSet" or "top10" or "aboveAverage" or "duplicateValues" or "uniqueValues";

    private static IReadOnlyList<XElement> RewriteRule(XElement rule, SheetEditPlan plan, StructuralReferences references, string owner, CancellationToken token, bool preservePopulation)
    {
        if (!references.HasRowChanges)
        {
            return new[] { new XElement(rule) };
        }

        var reference = (string)rule.Attribute("sqref")!;
        var inputRanges = reference.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(value => CellRange.Parse(value.Replace("$", string.Empty))).ToArray();
        var originalBase = inputRanges[0].First;
        var formulas = StructuralReferences.FormulaStores(rule).Select(StructuralReferences.FormulaText).ToArray();
        if (formulas.Length == 0)
        {
            var ranges = StructuralReferences.Expand(reference, plan);
            if (ranges.Count == 0)
            {
                return Array.Empty<XElement>();
            }

            var clone = new XElement(rule);
            clone.SetAttributeValue("sqref", string.Join(" ", ranges.Select(range => range.Reference)));
            return new[] { clone };
        }

        var tailBoundary = formulas.Max(formula => references.RuleTailBoundary(formula, owner, originalBase));
        var groups = new Dictionary<(int Context, string Key), RuleGroup>();
        foreach (var range in inputRanges)
        {
            foreach (var row in plan.Rows.Where(row => row.SourceRow >= range.First.Row && row.SourceRow <= range.Last.Row))
            {
                Add(row.SourceRow, row.OutputRow, row.OutputRow, row.OccurrenceId);
            }

            // Keep implicit trailing rows as one interval, including full-column rules.
            if (range.Last.Row > plan.SourceLastRow)
            {
                var first = Math.Max(range.First.Row, plan.SourceLastRow + 1);
                for (; first <= Math.Min(range.Last.Row, tailBoundary); first++)
                {
                    var output = references.FirstRow(plan, first)!.Value;
                    Add(first, output, output, 0);
                }

                if (first <= range.Last.Row)
                {
                    Add(first, references.FirstRow(plan, first)!.Value, references.FirstRow(plan, range.Last.Row)!.Value, 0);
                }
            }

            void Add(int sourceRow, int outputRow, int lastRow, int context)
            {
                token.ThrowIfCancellationRequested();
                var source = new CellAddress(sourceRow, range.First.Column);
                var output = new CellAddress(outputRow, range.First.Column);
                var updated = formulas.Select(formula => references.RewriteRule(formula, owner, originalBase, source, output, context)).ToArray();
                var key = string.Join("\0", updated.Select(formula => StructuralReferences.RuleKey(formula, output)));
                var groupKey = (preservePopulation ? 0 : references.RuleContext(plan, sourceRow, context), key);
                if (!groups.TryGetValue(groupKey, out var group))
                {
                    if (preservePopulation && groups.Count > 0)
                    {
                        throw new NotSupportedException("A population-dependent conditional format cannot preserve its formula references in one application set after row expansion.");
                    }

                    group = new RuleGroup(new XElement(rule));
                    var stores = StructuralReferences.FormulaStores(group.Element).ToArray();
                    for (var index = 0; index < stores.Length; index++)
                    {
                        StructuralReferences.SetFormulaText(stores[index], updated[index]);
                    }

                    groups.Add(groupKey, group);
                }

                group.Add(output, new CellAddress(lastRow, range.Last.Column));
            }
        }

        return groups.Values.Select(group =>
        {
            group.Element.SetAttributeValue("sqref", string.Join(" ", group.Ranges.Select(range => range.First.Equals(range.Last) ? range.First.ToString() : range.First + ":" + range.Last)));
            return group.Element;
        }).ToArray();
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

    private static void RewriteDefinedNames(XDocument workbook, StructuralReferences references, bool formulaAst = false)
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
                definition.Value = formulaAst ? references.RewriteFormula(definition.Value, names[scope]) : references.Rewrite(definition.Value, names[scope]);
            }
        }
    }

    private sealed class RuleGroup
    {
        internal RuleGroup(XElement element)
        {
            Element = element;
        }

        internal XElement Element { get; }

        internal List<(CellAddress First, CellAddress Last)> Ranges { get; } = new List<(CellAddress First, CellAddress Last)>();

        internal void Add(CellAddress first, CellAddress last)
        {
            if (Ranges.Count > 0)
            {
                var previous = Ranges[Ranges.Count - 1];
                if (previous.First.Column == first.Column && previous.Last.Column == last.Column && previous.Last.Row + 1 == first.Row)
                {
                    Ranges[Ranges.Count - 1] = (previous.First, last);
                    return;
                }
            }

            Ranges.Add((first, last));
        }
    }
}
