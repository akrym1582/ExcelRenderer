using System.Text.Json;
using ExcelRenderer;
using ExcelRenderer.Rendering;
using ExcelRenderer.TestFixtures;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.SkiaSharp;

using var culture = new ReaderSnapshot.InvariantCultureScope();
var root = args[0];
var destination = args[1];
var original = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(Path.Combine(root, "tests/ExcelRenderer.Tests/ReaderGolden/artifacts.json")))!;
var hashes = new Dictionary<string, Dictionary<string, string>>();
foreach (var path in original.Keys.Order())
{
    var outputs = new Dictionary<string, string>();
    foreach (var format in args.Length > 2 ? Array.Empty<OutputFormat>() : new[] { OutputFormat.Png, OutputFormat.Svg, OutputFormat.Markdown })
    {
        using var input = File.OpenRead(Path.Combine(root, path));
        using var capture = new ReaderSnapshot.Capture();
        await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = format,
            FontOptions = new() { AllowSystemFonts = false, UseFontPack = false, FontFiles = [Path.Combine(root, "third_party/NotoSansJP/NotoSansJP-Regular.ttf")] },
        }, capture);
        foreach (var (name, stream) in capture.Artifacts)
        {
            var bytes = stream.ToArray();
            var key = format + "/" + name;
            outputs.Add(key, ReaderSnapshot.ArtifactHash(format, bytes));
            var target = Path.Combine(destination, path, key);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
    }

    hashes.Add(path, outputs);
    var masks = new Dictionary<string, List<double[]>>();
    var manager = new FontManager(new() { AllowSystemFonts = false, UseFontPack = false, FontFiles = [Path.Combine(root, "third_party/NotoSansJP/NotoSansJP-Regular.ttf")] });
    foreach (var sheet in new ExcelReader(manager).Read(Path.Combine(root, path)).Sheets)
    {
        var document = new ReportLayoutEngine(new SkiaTextMeasurer(manager)).Layout(sheet);
        foreach (var page in document.Pages)
        {
            var rectangles = new List<double[]>();
            var commands = new DrawCommandGeneratorPass().Generate(new([page]));
            foreach (var line in Flatten(commands).OfType<DrawLineCommand>())
            {
                // Include the old inward Double's furthest stroke and antialiasing.
                var pad = (line.Style.Width * 4) + 2;
                rectangles.Add([Math.Min(line.X1, line.X2) - pad, Math.Min(line.Y1, line.Y2) - pad, Math.Max(line.X1, line.X2) + pad, Math.Max(line.Y1, line.Y2) + pad]);
            }

            masks.Add(sheet.Name + "-" + page.Number + ".png", rectangles);
        }
    }

    File.WriteAllText(Path.Combine(destination, path, "border-masks.json"), JsonSerializer.Serialize(masks));
}

Directory.CreateDirectory(destination);
if (args.Length <= 2)
{
    File.WriteAllText(Path.Combine(destination, "hashes.json"), JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true }) + "\n");
}

static IEnumerable<DrawCommand> Flatten(IEnumerable<DrawCommand> commands) => commands.SelectMany(command =>
    command.GetType().Name == "DrawViewportCommand"
        ? Flatten((IEnumerable<DrawCommand>)command.GetType().GetProperty("Commands", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(command)!)
        : new[] { command });
