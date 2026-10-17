using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.OpenXml.Input;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.OpenXml;

/// <summary>Writes conservative workbook edits into a separate staged package, with optional formula reference rewriting.</summary>
public static partial class OpenXmlWorkbookWriter
{
    private static readonly XNamespace Ns = XmlValues.Spreadsheet;

    /// <summary>Writes a validated final-placement plan and leaves both caller streams open.</summary>
    /// <param name="input">Validated seekable source at position zero.</param>
    /// <param name="output">Writable destination; untouched until stage success.</param>
    /// <param name="plan">Evaluated source-to-output occurrences.</param>
    /// <param name="buffering">Memory and temporary-file settings shared by stage and row stores.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <remarks>The final stream copy may partially write if the caller's destination fails.</remarks>
    public static void Write(Stream input, Stream output, WorkbookEditPlan plan, WorkbookInputOptions? buffering = null, CancellationToken cancellationToken = default)
    {
        if (input == null || output == null || plan == null)
        {
            throw new ArgumentNullException(input == null ? nameof(input) : output == null ? nameof(output) : nameof(plan));
        }

        if (ReferenceEquals(input, output) || !output.CanWrite)
        {
            throw new ArgumentException("Input and writable output must differ.");
        }

        var reasons = WorkbookWriteCapabilities.Inspect(input, cancellationToken, plan.RewriteFormulas);
        if (reasons.Count != 0)
        {
            throw new NotSupportedException("OpenXml editing is unsupported: " + string.Join(", ", reasons));
        }

        var options = buffering ?? new WorkbookInputOptions();
        var buffers = new InputBufferOptions { MemoryThresholdBytes = options.MemoryThresholdBytes, AllowTemporaryFiles = options.AllowTemporaryFiles, TemporaryDirectory = options.TemporaryDirectory };
        var paths = new Dictionary<int, string>();
        string? stylesPath;
        string? stringsPath;
        string workbookPath;
        bool date1904;
        var names = new Dictionary<string, SheetEditPlan>(StringComparer.OrdinalIgnoreCase);
        var sheetNames = new Dictionary<int, string>();
        input.Position = 0;
        using (var package = SpreadsheetDocument.Open(new LeaveOpenStream(input), false))
        {
            var workbook = package.WorkbookPart!;
            workbookPath = workbook.Uri.ToString().TrimStart('/');
            stylesPath = workbook.WorkbookStylesPart?.Uri.ToString().TrimStart('/');
            stringsPath = workbook.SharedStringTablePart?.Uri.ToString().TrimStart('/');
            date1904 = workbook.Workbook.WorkbookProperties?.Date1904?.Value ?? false;
            var index = 0;
            foreach (var sheet in workbook.Workbook.Sheets!.Elements<S.Sheet>())
            {
                paths.Add(++index, workbook.GetPartById(sheet.Id!.Value!).Uri.ToString().TrimStart('/'));
                sheetNames.Add(index, sheet.Name!.Value!);
            }
        }

        if (plan.Sheets.Select(sheet => sheet.SheetIndex).Distinct().Count() != paths.Count || plan.Sheets.Count != paths.Count || plan.Sheets.Any(sheet => !paths.ContainsKey(sheet.SheetIndex)))
        {
            throw new ArgumentException("A plan for every worksheet is required.", nameof(plan));
        }

        foreach (var sheet in plan.Sheets)
        {
            names.Add(sheetNames[sheet.SheetIndex], sheet);
            if (sheet.SourceLastRow is < 0 or > 1048576)
            {
                throw new ArgumentException("Invalid source row extent.", nameof(plan));
            }

            for (var index = 0; index < sheet.Rows.Count; index++)
            {
                var row = sheet.Rows[index];
                if (row.OutputRow != index + 1 || row.SourceRow < 1 || row.SourceRow > sheet.SourceLastRow || row.OutputRow > 1048576 || row.Values.Select(value => value.Column).Distinct().Count() != row.Values.Count || row.Values.Any(value => value.Column is < 1 or > 16384))
                {
                    throw new ArgumentException("Invalid final row placement or cell edit.", nameof(plan));
                }
            }
        }

        var formulas = new Dictionary<(int Sheet, int Row, int Column), string>();
        if (plan.RewriteFormulas)
        {
            input.Position = 0;
            using var formulaReader = OpenXmlWorkbookReader.Open(input, cancellationToken: cancellationToken);
            foreach (var sheet in formulaReader.SelectedWorksheetIndices)
            {
                foreach (var cell in formulaReader.EnumerateCells(sheet, cancellationToken))
                {
                    if (cell.Formula != null)
                    {
                        if (cell.HasCalculationMetadata || cell.Formula.Kind is not "normal" and not "shared" || !FormulaTextResolver.TryResolve(cell, out var text) || text == null)
                        {
                            throw new NotSupportedException("Unsupported formula " + (cell.HasCalculationMetadata ? "calculation metadata" : cell.Formula.Kind) + " at " + sheetNames[sheet] + "!" + cell.Address);
                        }

                        formulas.Add((sheet, cell.Address.Row, cell.Address.Column), text);
                    }
                }
            }
        }

        input.Position = 0;
        using var archive = new ZipArchive(new LeaveOpenStream(input), ZipArchiveMode.Read);
        using var stage = new SpillableBufferStream(buffers);
        var sources = new Dictionary<string, SheetSource>(StringComparer.Ordinal);
        try
        {
            foreach (var sheet in plan.Sheets)
            {
                var path = paths[sheet.SheetIndex];
                using var source = archive.GetEntry(path)!.Open();
                sources.Add(path, new SheetSource(source, sheet, buffers, cancellationToken));
            }

            XDocument? styles = DefaultStyles();
            if (stylesPath != null)
            {
                using var source = archive.GetEntry(stylesPath)!.Open();
                styles = XDocument.Load(source);
            }

            var stylesDirectory = workbookPath.LastIndexOf('/') is var slash && slash >= 0 ? workbookPath.Substring(0, slash + 1) : string.Empty;
            var addedStylesPath = stylesDirectory + "styles.xml";
            for (var suffix = 1; stylesPath is null && archive.GetEntry(addedStylesPath) is not null; suffix++)
            {
                addedStylesPath = stylesDirectory + "styles-integration-" + suffix.ToString(CultureInfo.InvariantCulture) + ".xml";
            }

            var workbookRelationshipsPath = stylesDirectory + "_rels/" + workbookPath.Substring(stylesDirectory.Length) + ".rels";
            var styleEdits = new Dictionary<(int Style, int Format), int>();
            long sharedReferences = 0;
            var replacements = new Dictionary<string, SpillableBufferStream>(StringComparer.Ordinal);
            try
            {
                var references = new StructuralReferences(names);
                RewriteStructures(input, archive, sources, replacements, references, buffers, cancellationToken, plan.RewriteFormulas);
                foreach (var item in sources)
                {
                    if (!plan.RewriteFormulas && !NeedsWorksheetEdit(item.Value.Plan) && !item.Value.Children.Any(child => child.Name.LocalName is "hyperlinks" or "conditionalFormatting" or "dataValidations"))
                    {
                        using var unmodified = archive.GetEntry(item.Key)!.Open();
                        sharedReferences = checked(sharedReferences + CountSharedReferences(unmodified));
                        continue;
                    }

                    var destination = new SpillableBufferStream(buffers);
                    replacements.Add(item.Key, destination);
                    sharedReferences = checked(sharedReferences + WriteSheet(item.Value, destination, styles, styleEdits, date1904, cancellationToken, plan.RewriteFormulas ? references : null, sheetNames[item.Value.Plan.SheetIndex], formulas));
                }

                using (var destination = new ZipArchive(stage, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var entry in archive.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var target = destination.CreateEntry(entry.FullName, CompressionLevel.Optimal).Open();
                        if (replacements.TryGetValue(entry.FullName, out var replacement))
                        {
                            replacement.Position = 0;
                            replacement.CopyTo(target);
                        }
                        else if (entry.FullName == workbookPath)
                        {
                            using var source = entry.Open();
                            var workbook = XDocument.Load(source);
                            if (sources.Values.Any(source => source.Plan.Rows.Count != source.Plan.SourceLastRow || source.Plan.Rows.Any(row => row.SourceRow != row.OutputRow)) && workbook.Root?.Element(Ns + "definedNames")?.HasElements == true)
                            {
                                RewriteDefinedNames(workbook, references, plan.RewriteFormulas);
                                workbook.Save(target);
                            }
                            else
                            {
                                using var original = entry.Open();
                                original.CopyTo(target);
                            }
                        }
                        else if (stylesPath is null && styleEdits.Count > 0 && entry.FullName == "[Content_Types].xml")
                        {
                            using var source = entry.Open();
                            var types = XDocument.Load(source);
                            types.Root!.Add(new XElement(types.Root.Name.Namespace + "Override", new XAttribute("PartName", "/" + addedStylesPath), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")));
                            types.Save(target);
                        }
                        else if (stylesPath is null && styleEdits.Count > 0 && entry.FullName == workbookRelationshipsPath)
                        {
                            using var source = entry.Open();
                            var relationships = XDocument.Load(source);
                            var id = "rIdIntegrationStyles";
                            while (relationships.Root!.Elements().Any(element => (string?)element.Attribute("Id") == id))
                            {
                                id += "_";
                            }

                            relationships.Root.Add(new XElement(relationships.Root.Name.Namespace + "Relationship", new XAttribute("Id", id), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), new XAttribute("Target", addedStylesPath.Substring(stylesDirectory.Length))));
                            relationships.Save(target);
                        }
                        else if (entry.FullName == stylesPath && styleEdits.Count > 0)
                        {
                            styles!.Save(target);
                        }
                        else if (entry.FullName == stringsPath)
                        {
                            using var source = entry.Open();
                            using var rootReader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                            rootReader.MoveToContent();
                            var originalCount = rootReader.GetAttribute("count");
                            using var original = entry.Open();
                            if (originalCount is null || long.Parse(originalCount, CultureInfo.InvariantCulture) == sharedReferences)
                            {
                                original.CopyTo(target);
                            }
                            else
                            {
                                RewriteSharedStringCount(original, target, sharedReferences);
                            }
                        }
                        else
                        {
                            using var source = entry.Open();
                            source.CopyTo(target);
                        }
                    }

                    if (stylesPath is null && styleEdits.Count > 0)
                    {
                        using var target = destination.CreateEntry(addedStylesPath, CompressionLevel.Optimal).Open();
                        styles!.Save(target);
                    }
                }
            }
            finally
            {
                foreach (var replacement in replacements.Values)
                {
                    replacement.Dispose();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (plan.RewriteFormulas)
            {
                stage.Position = 0;
                using var formulaPackage = SpreadsheetDocument.Open(stage, true);
                var workbookPart = formulaPackage.WorkbookPart!;
                if (workbookPart.CalculationChainPart != null)
                {
                    workbookPart.DeletePart(workbookPart.CalculationChainPart);
                }

                workbookPart.Workbook.CalculationProperties ??= new S.CalculationProperties();
                workbookPart.Workbook.CalculationProperties.FullCalculationOnLoad = true;
                workbookPart.Workbook.CalculationProperties.ForceFullCalculation = true;
                workbookPart.Workbook.Save();
            }

            stage.Position = 0;
            using (var validation = OpenXmlWorkbookReader.Open(stage, cancellationToken: cancellationToken))
            {
                foreach (var sheet in validation.SelectedWorksheetIndices)
                {
                    if (!plan.RewriteFormulas && validation.ReadWorksheetInfo(sheet, cancellationToken).HasFormulas)
                    {
                        throw new InvalidDataException("Generated formula-free workbook contains a formula.");
                    }
                }
            }

            stage.Position = 0;
            using (var package = SpreadsheetDocument.Open(new LeaveOpenStream(stage), false))
            {
                WorkbookRelationships.Validate(package, cancellationToken);
            }

            OperationMetrics.Report("writer.stageBytes", stage.Length);
            stage.Position = 0;
            Copy(stage, output, cancellationToken);
        }
        finally
        {
            foreach (var source in sources.Values)
            {
                source.Dispose();
            }
        }
    }

    private static bool NeedsWorksheetEdit(SheetEditPlan sheet) => sheet.ExplicitPageBreak || sheet.RowBreaks.Count > 0 || sheet.ColumnBreaks.Count > 0 ||
        sheet.Rows.Count != sheet.SourceLastRow || sheet.Rows.Any(row => row.SourceRow != row.OutputRow || row.Values.Count > 0);

    private static long CountSharedReferences(Stream source)
    {
        using var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        long count = 0;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "c" && reader.NamespaceURI == Ns.NamespaceName && reader.GetAttribute("t") == "s")
            {
                count = checked(count + 1);
            }
        }

        return count;
    }

    private static XDocument DefaultStyles() => new(new XElement(
        Ns + "styleSheet",
        new XElement(Ns + "fonts", new XAttribute("count", 1), new XElement(Ns + "font", new XElement(Ns + "sz", new XAttribute("val", 11)), new XElement(Ns + "name", new XAttribute("val", "Calibri")))),
        new XElement(Ns + "fills", new XAttribute("count", 2), new XElement(Ns + "fill", new XElement(Ns + "patternFill", new XAttribute("patternType", "none"))), new XElement(Ns + "fill", new XElement(Ns + "patternFill", new XAttribute("patternType", "gray125")))),
        new XElement(Ns + "borders", new XAttribute("count", 1), new XElement(Ns + "border")),
        new XElement(Ns + "cellStyleXfs", new XAttribute("count", 1), new XElement(Ns + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0))),
        new XElement(Ns + "cellXfs", new XAttribute("count", 1), new XElement(Ns + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0))),
        new XElement(Ns + "cellStyles", new XAttribute("count", 1), new XElement(Ns + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0)))));

    private static void Copy(Stream input, Stream output, CancellationToken token)
    {
        var buffer = new byte[81920];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return;
            }

            output.Write(buffer, 0, read);
        }
    }

    private static string EncodeText(string source)
    {
        var result = new StringBuilder(source.Length);
        for (var index = 0; index < source.Length; index++)
        {
            var value = source[index];
            if (value == '_' && index + 6 < source.Length && source[index + 1] == 'x' && source[index + 6] == '_' &&
                ushort.TryParse(source.Substring(index + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            {
                result.Append("_x005F_");
            }
            else if ((value < 32 && value is not '\t' and not '\n' and not '\r') || value is '\uFFFE' or '\uFFFF')
            {
                result.Append("_x").Append(((int)value).ToString("X4", CultureInfo.InvariantCulture)).Append('_');
            }
            else
            {
                result.Append(value);
            }
        }

        return result.ToString();
    }

    private static long WriteSheet(SheetSource source, Stream output, XDocument? styles, Dictionary<(int Style, int Format), int> styleEdits, bool date1904, CancellationToken token, StructuralReferences? formulaReferences, string sheetName, Dictionary<(int Sheet, int Row, int Column), string> formulas)
    {
        var children = source.Children;
        var merges = RewriteMerges(source);
        var rowBreaks = RewriteRowBreaks(source);
        var columnBreaks = children.FirstOrDefault(child => child.Name.LocalName == "colBreaks")?.Elements().Select(child => (int?)child.Attribute("id") ?? 0).Concat(source.Plan.ColumnBreaks).Distinct().OrderBy(value => value).ToArray() ?? source.Plan.ColumnBreaks.ToArray();
        var lastColumn = source.LastColumn;
        using var xml = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false, NewLineHandling = NewLineHandling.Entitize });
        xml.WriteStartDocument();
        xml.WriteStartElement("worksheet", Ns.NamespaceName);
        foreach (var attribute in source.Root.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration || attribute.Name.LocalName != "xmlns"))
        {
            if (attribute.IsNamespaceDeclaration)
            {
                xml.WriteAttributeString("xmlns", attribute.Name.LocalName, null, attribute.Value);
            }
            else
            {
                xml.WriteAttributeString(attribute.Name.LocalName, attribute.Name.NamespaceName, attribute.Value);
            }
        }

        if (source.Plan.ExplicitPageBreak && children.All(child => child.Name.LocalName != "sheetPr"))
        {
            children.Insert(0, new XElement(Ns + "sheetPr", new XElement(Ns + "pageSetUpPr", new XAttribute("fitToPage", 0))));
        }

        if (source.Plan.ExplicitPageBreak && children.All(child => child.Name.LocalName != "pageSetup"))
        {
            var position = children.FindIndex(child => child.Name.LocalName is "headerFooter" or "rowBreaks" or "colBreaks" or "customProperties" or "cellWatches" or "ignoredErrors" or "drawing" or "legacyDrawing" or "tableParts");
            children.Insert(position < 0 ? children.Count : position, new XElement(Ns + "pageSetup", new XAttribute("scale", 100)));
        }

        long sharedReferences = 0;
        var breaksWritten = false;
        foreach (var original in children)
        {
            token.ThrowIfCancellationRequested();
            var child = new XElement(original);
            if (!breaksWritten && child.Name.LocalName is "rowBreaks" or "colBreaks" or "customProperties" or "cellWatches" or "ignoredErrors" or "smartTags" or "drawing" or "legacyDrawing" or "legacyDrawingHF" or "picture" or "oleObjects" or "controls" or "webPublishItems" or "tableParts" or "extLst")
            {
                WriteBreaks(xml, "rowBreaks", rowBreaks, 16383);
                WriteBreaks(xml, "colBreaks", columnBreaks, 1048575);
                breaksWritten = true;
            }

            switch (child.Name.LocalName)
            {
                case "dimension":
                    var lastRow = source.OutputRows().Select(row => row.OutputRow).DefaultIfEmpty(0).Max();
                    child.SetAttributeValue("ref", lastRow == 0 ? "A1" : "A1:" + new CellAddress(lastRow, Math.Max(1, lastColumn)));
                    break;
                case "sheetPr":
                    if (source.Plan.ExplicitPageBreak)
                    {
                        var setup = child.Element(Ns + "pageSetUpPr");
                        if (setup == null)
                        {
                            setup = new XElement(Ns + "pageSetUpPr");
                            child.Add(setup);
                        }

                        setup.SetAttributeValue("fitToPage", 0);
                    }

                    break;
                case "sheetData":
                    xml.WriteStartElement("sheetData", Ns.NamespaceName);
                    foreach (var occurrence in source.OutputRows())
                    {
                        token.ThrowIfCancellationRequested();
                        var row = source.ReadRow(occurrence.SourceRow);
                        row.SetAttributeValue("r", occurrence.OutputRow);
                        var column = 0;
                        foreach (var cell in row.Elements(Ns + "c"))
                        {
                            column = cell.Attribute("r") is { } address ? CellAddress.Parse(address.Value.Replace("$", string.Empty)).Column : column + 1;
                            cell.SetAttributeValue("r", new CellAddress(occurrence.OutputRow, column));
                            if (formulaReferences != null && formulas.TryGetValue((source.Plan.SheetIndex, occurrence.SourceRow, column), out var formula))
                            {
                                cell.Elements().Where(element => element.Name.LocalName is "f" or "v" or "is").Remove();
                                cell.Attribute("t")?.Remove();
                                cell.Add(new XElement(Ns + "f", formulaReferences.RewriteFormula(formula, sheetName, occurrence, column)));
                            }
                        }

                        foreach (var edit in occurrence.Values)
                        {
                            var address = new CellAddress(occurrence.OutputRow, edit.Column).ToString();
                            var cell = row.Elements(Ns + "c").FirstOrDefault(cell => (string?)cell.Attribute("r") == address);
                            if (cell == null)
                            {
                                cell = new XElement(Ns + "c", new XAttribute("r", address));
                                var following = row.Elements(Ns + "c").FirstOrDefault(item => CellAddress.Parse((string)item.Attribute("r")!).Column > edit.Column);
                                if (following == null)
                                {
                                    row.Add(cell);
                                }
                                else
                                {
                                    following.AddBeforeSelf(cell);
                                }
                            }

                            WriteValue(cell, row, source, edit, styles, styleEdits, date1904);
                        }

                        var cells = row.Elements(Ns + "c").ToArray();
                        row.SetAttributeValue("spans", cells.Length == 0 ? null : CellAddress.Parse((string)cells[0].Attribute("r")!).Column.ToString(CultureInfo.InvariantCulture) + ":" + CellAddress.Parse((string)cells[cells.Length - 1].Attribute("r")!).Column.ToString(CultureInfo.InvariantCulture));
                        sharedReferences = checked(sharedReferences + cells.LongCount(cell => (string?)cell.Attribute("t") == "s"));
                        row.WriteTo(xml);
                    }

                    xml.WriteEndElement();
                    continue;
                case "mergeCells":
                    if (merges.Count == 0)
                    {
                        continue;
                    }

                    child = new XElement(Ns + "mergeCells", new XAttribute("count", merges.Count), merges.Select(range => new XElement(Ns + "mergeCell", new XAttribute("ref", range))));
                    break;
                case "rowBreaks":
                case "colBreaks":
                    continue;
                case "pageSetup":
                    if (source.Plan.ExplicitPageBreak)
                    {
                        child.SetAttributeValue("scale", (int?)child.Attribute("scale") is > 0 ? (int)child.Attribute("scale")! : 100);
                        child.Attribute("fitToWidth")?.Remove();
                        child.Attribute("fitToHeight")?.Remove();
                    }

                    break;
            }

            child.WriteTo(xml);
        }

        if (source.Plan.ExplicitPageBreak && children.All(child => child.Name.LocalName != "pageSetup"))
        {
            new XElement(Ns + "pageSetup", new XAttribute("scale", 100)).WriteTo(xml);
        }

        if (!breaksWritten)
        {
            WriteBreaks(xml, "rowBreaks", rowBreaks, 16383);
            WriteBreaks(xml, "colBreaks", columnBreaks, 1048575);
        }

        xml.WriteEndElement();
        xml.WriteEndDocument();
        return sharedReferences;
    }

    private static void WriteValue(XElement cell, XElement row, SheetSource source, CellValueEdit edit, XDocument? styles, Dictionary<(int Style, int Format), int> styleEdits, bool date1904)
    {
        cell.Elements().Where(child => child.Name.LocalName is "v" or "is" or "f").Remove();
        cell.Attribute("t")?.Remove();
        var value = edit.Value;
        if (value == null)
        {
            return;
        }

        if (value is string text)
        {
            if (text.Length > 32767)
            {
                throw new InvalidDataException("Excel cell strings cannot exceed 32767 characters.");
            }

            if (text.StartsWith("'", StringComparison.Ordinal))
            {
                text = text.Substring(1);
                var original = (int?)cell.Attribute("s") ?? ((string?)row.Attribute("customFormat") is "1" or "true" ? (int?)row.Attribute("s") : null) ?? source.ColumnStyle(edit.Column);
                var formats = styles?.Root?.Element(Ns + "cellXfs") ?? throw new InvalidDataException("Missing cell formats.");
                var key = (original, -1);
                if (!styleEdits.TryGetValue(key, out var index))
                {
                    var originalStyle = formats.Elements(Ns + "xf").ElementAtOrDefault(original) ?? throw new InvalidDataException("Invalid cell style.");
                    index = formats.Elements(Ns + "xf").Count();
                    var added = new XElement(originalStyle);
                    added.SetAttributeValue("quotePrefix", 1);
                    formats.Add(added);
                    formats.SetAttributeValue("count", index + 1);
                    styleEdits.Add(key, index);
                }

                cell.SetAttributeValue("s", index);
            }

            cell.SetAttributeValue("t", "inlineStr");
            cell.Add(new XElement(Ns + "is", new XElement(Ns + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), EncodeText(text))));
            return;
        }

        double number;
        if (value is bool boolean)
        {
            cell.SetAttributeValue("t", "b");
            cell.Add(new XElement(Ns + "v", boolean ? "1" : "0"));
            return;
        }

        if (value is DateTime date)
        {
            number = SerialDate(date, date1904);
        }
        else if (value is TimeSpan duration)
        {
            number = duration.TotalDays;
        }
        else
        {
            number = (double)value;
        }

        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            throw new InvalidDataException("Excel numeric values must be finite.");
        }

        if (value is DateTime or TimeSpan)
        {
            var original = (int?)cell.Attribute("s") ?? ((string?)row.Attribute("customFormat") is "1" or "true" ? (int?)row.Attribute("s") : null) ?? source.ColumnStyle(edit.Column);
            var formats = styles?.Root?.Element(Ns + "cellXfs");
            var originalStyle = formats?.Elements(Ns + "xf").ElementAtOrDefault(original);
            if (originalStyle == null)
            {
                throw new NotSupportedException("Typed date/time edits require an existing style table.");
            }

            if (((int?)originalStyle.Attribute("numFmtId") ?? 0) == 0)
            {
                var key = (original, value is TimeSpan ? 46 : 14);
                if (!styleEdits.TryGetValue(key, out var index))
                {
                    index = formats!.Elements(Ns + "xf").Count();
                    var added = new XElement(originalStyle);
                    added.SetAttributeValue("numFmtId", key.Item2);
                    added.SetAttributeValue("applyNumberFormat", 1);
                    formats.Add(added);
                    formats.SetAttributeValue("count", index + 1);
                    styleEdits.Add(key, index);
                }

                cell.SetAttributeValue("s", index);
            }
        }

        cell.Add(new XElement(Ns + "v", number.ToString("R", CultureInfo.InvariantCulture)));
    }

    private static double SerialDate(DateTime date, bool date1904) => date1904 ? (date - new DateTime(1904, 1, 1)).TotalDays :
        (date - new DateTime(1899, 12, 31)).TotalDays + (date >= new DateTime(1900, 3, 1) ? 1 : 0);

    private static void RewriteSharedStringCount(Stream input, Stream output, long count)
    {
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        using var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false, NewLineHandling = NewLineHandling.Entitize });
        reader.MoveToContent();
        writer.WriteStartElement("sst", Ns.NamespaceName);
        while (reader.MoveToNextAttribute())
        {
            if (reader.LocalName != "count" && reader.Name != "xmlns")
            {
                writer.WriteAttributeString(reader.Prefix, reader.LocalName, reader.NamespaceURI, reader.Value);
            }
        }

        writer.WriteAttributeString("count", count.ToString(CultureInfo.InvariantCulture));
        reader.MoveToElement();
        if (!reader.IsEmptyElement)
        {
            reader.Read();
            while (reader.NodeType != XmlNodeType.EndElement)
            {
                writer.WriteNode(reader, true);
            }
        }

        writer.WriteEndElement();
    }
}
