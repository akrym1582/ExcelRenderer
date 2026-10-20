using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExcelRenderer;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.Pdf;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;

if (args.Length == 2 && args[0] == "--managed-fonts")
{
    ExcelRenderer.PdfComparison.ManagedFontEvidence.Run(args[1]);
    return;
}

if (args.Length == 3 && args[0] == "--api-diff")
{
    ExcelRenderer.PdfComparison.ApiSurfaceAudit.Compare(args[1], args[2]);
    return;
}

if (args.Length == 2 && args[0] == "--audit-assemblies")
{
    ExcelRenderer.PdfComparison.AssemblyReferenceAudit.Run(args[1]);
    return;
}

var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("artifacts/pdf-comparison");
var fixtureFilter = args.Length > 1 ? args[1] : "all";
Directory.CreateDirectory(output);
var fonts = new FontManager(new FontOptions { AllowSystemFonts = false });
var reports = new List<object>();
PdfRenderObserver.Timestamp = new DateTime(2026, 10, 18, 12, 34, 56, DateTimeKind.Local);
var texts = new[]
{
    "日本語 山銀行 金額 日⽇ 山⼭",
    "辻\U000E0102辻\U000E0103葛\U000E0102",
    "𠮷野家 😀",
    "ASCII  日本語 e\u0301  A B",
};
var style = CellStyle.Default with { Font = new("Noto Sans JP", 12) };
var measurer = new SkiaTextMeasurer(fonts);
var commands = texts.Select((text, index) => (DrawCommand)new DrawTextCommand(1, new(20, 20 + (index * 30), 500, 25), text, style)
{
    TextLayout = measurer.Layout(text, style.Font, 500, false),
}).Concat(new DrawCommand[]
{
    new DrawLineCommand(1, 20, 170, 500, 170, new(1, new(0, 0, 0))),
    new FillRectangleCommand(1, new(20, 180, 60, 20), new(32, 100, 180)),
}).ToArray();

if (fixtureFilter is "all" or "commands")
{
    var sizes = new[] { new PageSettings(595.275590551, 841.88976378), new PageSettings(841.88976378, 1190.5511811), new PageSettings(612, 792), new PageSettings(421.123, 333.456) };
    for (var index = 0; index < sizes.Length; index++)
    {
        var settings = sizes[index];
        var pageCommands = index == 3
            ? commands.Concat([new DrawLineCommand(1, settings.Width - .12, 210, settings.Width - .12, 230, new(.2, new(0, 0, 0)))]).ToArray()
            : commands;
        var name = $"commands-{index + 1}";
        var directory = Path.Combine(output, name);
        Directory.CreateDirectory(directory);
        var original = Path.Combine(directory, "pdfsharp.pdf");
        CopyBaseline(name, original);

        var descriptor = new RenderPageDescriptor(0, "commands", 1, 1, 1, settings.Width, settings.Height);
        using (var writer = new SkiaPdfDocumentWriter(fonts, CancellationToken.None)
        {
            BeforeFinalize = source =>
            {
                using var raw = File.Create(Path.Combine(directory, "skiasharp-raw.pdf"));
                source.CopyTo(raw);
            },
        })
        {
            writer.Collect(pageCommands);
            writer.Prepare();
            writer.Append(settings, descriptor, pageCommands, [], diagnostic => throw new InvalidOperationException(diagnostic.Message));
            using var stream = File.Create(Path.Combine(directory, "skiasharp.pdf"));
            writer.Complete(stream);
        }

        if (index == 0)
        {
            using var full = new SkiaPdfDocumentWriter(fonts, CancellationToken.None, fullEmbed: true);
            full.Collect(pageCommands);
            full.Prepare();
            full.Append(settings, descriptor, pageCommands, [], _ => { });
            using var stream = File.Create(Path.Combine(directory, "skiasharp-full.pdf"));
            full.Complete(stream);
        }

        File.WriteAllLines(Path.Combine(directory, "commands.jsonl"), pageCommands.Select(Fingerprint));
        var expected = new { name, path = "commands", expectedText = texts, width = settings.Width, height = settings.Height, sameCommands = true, commandSha256 = CommandHash(pageCommands) };
        File.WriteAllText(Path.Combine(directory, "expected.json"), JsonSerializer.Serialize(expected, new JsonSerializerOptions { WriteIndented = true }));
        reports.Add(expected);
    }
}

if (fixtureFilter is "all" or "links")
{
    var directory = Path.Combine(output, "links");
    Directory.CreateDirectory(directory);
    CopyBaseline("links", Path.Combine(directory, "pdfsharp.pdf"));
    CaptureNative(directory);
    var sizes = new[] { new PageSettings(595.275590551, 841.88976378), new PageSettings(421.123, 333.456) };
    IReadOnlyList<ResolvedPdfHyperlink>[] links =
    [
        [new(new(20, 210, 100, 20), null, 2, 20, 20, sizes[1].Height, "forward tooltip"), new(new(140, 210, 100, 20), "https://example.com/a?q=a%20b", null, null, null, null, "URL tooltip")],
        [new(new(20, 210, 100, 20), null, 1, 20, 20, sizes[0].Height, "backward tooltip")],
    ];
    foreach (var backend in new[] { PdfBackend.SkiaSharp })
    {
        using IPdfDocumentWriter writer = new SkiaPdfDocumentWriter(fonts, CancellationToken.None);
        if (writer.RequiresFontCollection)
        {
            writer.Collect(commands);
        }

        writer.Prepare();
        for (var index = 0; index < sizes.Length; index++)
        {
            writer.Append(sizes[index], new(0, "links", index + 1, index + 1, index + 1, sizes[index].Width, sizes[index].Height), commands, links[index], _ => { });
        }

        using var stream = File.Create(Path.Combine(directory, $"{backend.ToString().ToLowerInvariant()}.pdf"));
        writer.Complete(stream);
    }

    var expected = new { name = "links", path = "commands", expectedText = texts, links, commandSha256 = CommandHash(commands) };
    File.WriteAllText(Path.Combine(directory, "expected.json"), JsonSerializer.Serialize(expected, new JsonSerializerOptions { WriteIndented = true }));
    reports.Add(expected);
}

var metricSamples = from size in new[] { 6d, 9, 10, 12, 24, 10.125 }
                    from text in new[] { "ABC xyz  0123456789 ", "日本語 帳票 ", new string('A', 1000), string.Empty, "\n", "e\u0301 𠮷" }
                    select new { text, size, layout = measurer.Layout(text, new("Noto Sans JP", size), 10000, false) };
File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(metricSamples.Select(item => new { item.text, item.size, item.layout.Size, lines = item.layout.Lines.Select(line => new { line.Text, line.Width, line.Ascent, line.Descent, line.Leading, line.Baseline }) }), new JsonSerializerOptions { WriteIndented = true }));

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var generated = Path.Combine(output, "inputs");
Directory.CreateDirectory(generated);
WorkbookFixtures.Create(generated);

foreach (var scene in CompatibilityFixtures.Create(measurer).Where(scene => fixtureFilter is "all" || fixtureFilter == scene.Name))
{
    var directory = Path.Combine(output, scene.Name);
    Directory.CreateDirectory(directory);
    CopyBaseline(Path.GetFileName(directory), Path.Combine(directory, "pdfsharp.pdf"));
    CaptureNative(directory);
    foreach (var backend in new[] { PdfBackend.SkiaSharp })
    {
        using IPdfDocumentWriter writer = new SkiaPdfDocumentWriter(fonts, CancellationToken.None);
        if (writer.RequiresFontCollection)
        {
            writer.Collect(scene.Commands);
        }

        writer.Prepare();
        writer.Append(new(421.123, 400.456), new(0, scene.Name, 1, 1, 1, 421.123, 400.456), scene.Commands, [], diagnostic => throw new InvalidOperationException(diagnostic.Message));
        using var stream = File.Create(Path.Combine(directory, $"{backend.ToString().ToLowerInvariant()}.pdf"));
        writer.Complete(stream);
    }

    File.WriteAllLines(Path.Combine(directory, "commands.jsonl"), scene.Commands.Select(Fingerprint));
    var expected = new { name = scene.Name, path = "features", sameCommands = true, expectedText = scene.ExpectedText, regions = scene.Regions.Select(region => new { name = region.Name, bounds = region.Bounds }), commandSha256 = CommandHash(scene.Commands) };
    File.WriteAllText(Path.Combine(directory, "expected.json"), JsonSerializer.Serialize(expected, new JsonSerializerOptions { WriteIndented = true }));
    reports.Add(expected);
}

var samples = new Dictionary<string, string>
{
    ["invoice"] = "samples/mapping/invoice/invoice.xlsx",
    ["japanese-grid-report"] = "samples/excel/japanese-grid-report.xlsx",
    ["rich-range"] = "tests/Shared/RetainedWorkbooks/RichTextAcceptanceTests.RangeTrimAndMergedCellRetainRichTextInAllOutputFormats.79F50EFE1F4F079D.xlsx",
    ["rich-theme"] = "tests/Shared/RetainedWorkbooks/RichTextAcceptanceTests.ThemeTintAndInheritedRunAttributesAreEffective.A180499274FB08F0.xlsx",
    ["rich-rotation"] = "tests/Shared/RetainedWorkbooks/RichTextAcceptanceTests.UnsupportedDecorationAndRotationPreserveTextAndReportOnce.74E94B16747ABDC2.xlsx",
    ["image-anchor"] = "tests/Shared/RetainedWorkbooks/LayoutPassTests.ExcelReader_preserves_picture_anchor_crop_and_transform.7F9B1506D1CCAB03.xlsx",
    ["drawing-anchors"] = "tests/Shared/RetainedWorkbooks/DrawingAnchorWorkbookFixture.Create.0F858476D16CBFD1.xlsx",
    ["shape-workbook"] = "tests/Shared/RetainedWorkbooks/ShapeAndFontTests.CreateWorkbookWithShapes.C704163BF33ECFBD.xlsx",
    ["header-footer"] = "tests/Shared/RetainedWorkbooks/LayoutPassTests.ExcelReader_reads_worksheet_header_and_footer.1A25B17C5123DEF2.xlsx",
    ["links-selected"] = "tests/Shared/RetainedWorkbooks/RangeTrimHyperlinkTests.Fixture.FB83DA40DBCCF2CE.xlsx",
    ["links-none"] = "tests/Shared/RetainedWorkbooks/RangeTrimHyperlinkTests.Fixture.FB83DA40DBCCF2CE.xlsx",
    ["blank-workbook"] = "tests/Shared/RetainedWorkbooks/RangeTrimHyperlinkTests.Hidden_only_selection_preserves_empty_trimmed_and_continuous_pages.9415BE3E2E0D0106.xlsx",
    ["pages-selection"] = Path.Combine(generated, "pages.xlsx"),
    ["missing-glyph"] = Path.Combine(generated, "missing.xlsx"),
};
foreach (var sample in samples.Where(sample => fixtureFilter == "all" || fixtureFilter == sample.Key))
{
    var directory = Path.Combine(output, sample.Key);
    Directory.CreateDirectory(directory);
    CopyBaseline(sample.Key, Path.Combine(directory, "pdfsharp.pdf"));
    CaptureNative(directory);
    var readerSnapshot = ExcelRenderer.PdfComparison.ReaderSnapshot.Capture(Path.Combine(root, sample.Value), fonts, Path.Combine(directory, "mdw-diagnostics.json"));
    File.WriteAllText(Path.Combine(directory, "reader-snapshot.json"), JsonSerializer.Serialize(readerSnapshot, new JsonSerializerOptions { WriteIndented = true }));
    using var frozenReader = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests/Shared/PdfRemovalBaseline", sample.Key, "reader-snapshot.json")));
    if (!JsonElement.DeepEquals(readerSnapshot, frozenReader.RootElement))
    {
        throw new InvalidOperationException($"{sample.Key}: source row/column/MDW, merge, print settings or rich runs changed.");
    }

    var results = new Dictionary<PdfBackend, ConversionResult>();
    var fingerprints = new Dictionary<PdfBackend, Dictionary<int, string>>();
    foreach (var backend in new[] { PdfBackend.SkiaSharp })
    {
        var pageHashes = new Dictionary<int, string>();
        fingerprints.Add(backend, pageHashes);
        PdfRenderObserver.Current = (page, pageCommands) =>
        {
            File.WriteAllLines(Path.Combine(directory, $"{backend.ToString().ToLowerInvariant()}-page-{page.OutputPageNumber}-commands.jsonl"), pageCommands.Select(Fingerprint));
            var fingerprint = CommandHash(pageCommands);
            if (pageHashes.TryGetValue(page.OutputPageNumber!.Value, out var previous) && previous != fingerprint)
            {
                throw new InvalidOperationException("Collection and drawing pass command fingerprints differ.");
            }

            pageHashes[page.OutputPageNumber!.Value] = fingerprint;
            if (backend == PdfBackend.SkiaSharp && page.OutputPageNumber == 1 && sample.Key is "invoice" or "japanese-grid-report")
            {
                // These small XLSX fixtures are one page. Use their exact finalized
                // commands for the comparison-only full-font size/pixel oracle.
                using var full = new SkiaPdfDocumentWriter(fonts, CancellationToken.None, fullEmbed: true);
                full.Collect(pageCommands);
                full.Prepare();
                full.Append(new(page.WidthPoints, page.HeightPoints), page, pageCommands, [], _ => { });
                using var fullOutput = File.Create(Path.Combine(directory, "skiasharp-full.pdf"));
                full.Complete(fullOutput);
            }
        };
        using var input = File.OpenRead(Path.Combine(root, sample.Value));
        using var stream = File.Create(Path.Combine(directory, $"{backend.ToString().ToLowerInvariant()}.pdf"));
        var request = new RenderRequest
        {
            OutputFormat = OutputFormat.Pdf,
            PdfBackend = backend,
            ReadOptions = ExcelRenderer.PdfComparison.ReaderSnapshot.HistoricalReadOptions,
            FontOptions = sample.Key == "missing-glyph"
                ? new() { AllowSystemFonts = false, UseFontPack = false, Registrations = [new("Noto Sans JP", Path.Combine(root, "third_party/NotoSansJP/NotoSansJP-Regular.ttf"))], FallbackFamilies = ["Noto Sans JP"] }
                : new() { AllowSystemFonts = false },
            Hyperlinks = sample.Key == "links-none" ? HyperlinkMode.None : HyperlinkMode.Preserve,
            Selection = sample.Key switch
            {
                "rich-range" => new() { Ranges = [new("rich", new(new(1, 1), new(3, 3)))] },
                "links-selected" => new() { SheetNames = ["Sheet1"], Ranges = [new("Sheet1", new(new(2, 2), new(8, 4)))] },
                "pages-selection" => new() { SheetNames = ["Second", "First"], Pages = [1, 3] },
                _ => new(),
            },
            Trim = new() { Enabled = sample.Key is "rich-range" or "links-selected" },
        };
        results[backend] = await ExcelConverter.RenderAsync(input, request, new SingleStreamOutputSink(stream));
    }

    PdfRenderObserver.Current = null;
    PdfRenderObserver.NativePdf = null;
    var left = JsonSerializer.Serialize(results[PdfBackend.SkiaSharp].Pages);
    File.WriteAllText(Path.Combine(directory, "conversion-diagnostics.json"), JsonSerializer.Serialize(results[PdfBackend.SkiaSharp].Diagnostics, new JsonSerializerOptions { WriteIndented = true }));
    var expected = new { name = sample.Key, path = "e2e", commandFingerprints = fingerprints[PdfBackend.SkiaSharp], pagePlanSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(left))), pages = results[PdfBackend.SkiaSharp].Pages, diagnostics = ExcelRenderer.PdfComparison.ReaderSnapshot.HistoricalDiagnostics(results[PdfBackend.SkiaSharp].Diagnostics) };
    File.WriteAllText(Path.Combine(directory, "expected.json"), JsonSerializer.Serialize(expected, new JsonSerializerOptions { WriteIndented = true }));
    reports.Add(expected);
}

if (reports.Count == 0)
{
    throw new ArgumentException("Unknown fixture. Use all, commands, links, invoice, or japanese-grid-report.");
}

var borderContracts = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "tools/ExcelRenderer.PdfComparison/shared-border-contracts.json")))!;
foreach (var report in reports)
{
    var actual = JsonSerializer.SerializeToElement(report);
    var name = actual.GetProperty("name").GetString()!;
    var baseline = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/Shared/PdfRemovalBaseline", name, "expected.json"));
    var frozen = JsonNode.Parse(File.ReadAllText(baseline))!;
    if (borderContracts["full"]?[name] is { } borderContract)
    {
        // Only the reviewed cell-border fingerprints change. Page plans,
        // diagnostics and every other frozen report field remain mandatory.
        frozen["commandFingerprints"] = borderContract["commandFingerprints"]!.DeepClone();
    }

    if (!JsonNode.DeepEquals(JsonNode.Parse(actual.GetRawText()), frozen))
    {
        throw new InvalidOperationException($"{name}: finalized commands, geometry, page plan or diagnostics differ from the frozen baseline.");
    }
}

File.WriteAllText(Path.Combine(output, "generation.json"), JsonSerializer.Serialize(new { sdkRuntime = Environment.Version.ToString(), os = Environment.OSVersion.ToString(), fixtures = reports }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(output);

static string CommandHash(IEnumerable<DrawCommand> commands) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", commands.Select(Fingerprint)))));

static string Fingerprint(DrawCommand command)
{
    object details = command switch
    {
        DrawTextCommand text => new
        {
            text.Bounds,
            text.Text,
            text.Style,
            text.TextLayout?.EffectiveFontSize,
            lines = text.TextLayout?.Lines.Select(line => new
            {
                line.Text,
                line.Width,
                line.Ascent,
                line.Descent,
                runs = line.Runs.Select(run => new { run.X, run.Advance, run.Font, run.Run.Text, run.Run.SourceText, run.Run.GlyphId, run.Run.ColorEmojiGlyphId, run.Run.Font.FaceId }),
            }),
        },
        DrawViewportCommand viewport => new { viewport.Clip, viewport.OffsetX, viewport.OffsetY, children = viewport.Commands.Select(Fingerprint) },
        DrawShapeCommand shape => new { shape.Bounds, shape.Shape, shape.ClipBounds, text = shape.TextCommand is null ? null : Fingerprint(shape.TextCommand) },
        DrawImageCommand image => new { image.Bounds, image.Crop, image.Rotation, image.ClipBounds, sha256 = Convert.ToHexString(SHA256.HashData(image.ImageBytes)) },
        _ => (object)command.ToString(),
    };
    return command.GetType().Name + JsonSerializer.Serialize(details);
}

static void CopyBaseline(string name, string target)
{
    var baseline = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/Shared/PdfRemovalBaseline", name, "pdfsharp.pdf"));
    File.Copy(baseline, target, true);
    File.Copy(Path.Combine(Path.GetDirectoryName(baseline)!, "skiasharp.pdf"), Path.Combine(Path.GetDirectoryName(target)!, "baseline-skiasharp.pdf"), true);
}

static void CaptureNative(string directory) => PdfRenderObserver.NativePdf = source =>
{
    using var raw = File.Create(Path.Combine(directory, "skiasharp-raw.pdf"));
    source.CopyTo(raw);
};
