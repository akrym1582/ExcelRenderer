using System.Text.Json;
using ExcelRenderer;
using ExcelRenderer.Excel;
using ExcelRenderer.Rendering;
using ExcelRenderer.TestFixtures;
using var culture = new ReaderSnapshot.InvariantCultureScope();
var root = Path.GetFullPath(args[0]);
var paths = new[] { "samples/excel/japanese-grid-report.xlsx", "samples/mapping/invoice/invoice.xlsx", "samples/mapping/invoice/page-breaks/invoice.xlsx" }.Select(p => Path.Combine(root, p));
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var artifacts = new Dictionary<string, Dictionary<string, string>>();
foreach (var path in paths)
{
    var name = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    var key = ReaderSnapshot.Hash(System.Text.Encoding.UTF8.GetBytes(name));
    var model = new ExcelReader().Read(path, new() { ColumnWidthMetrics = new() { Mode = ColumnWidthMetricsMode.RenderingFontLegacy } });
    File.WriteAllText(Path.Combine(output, key + ".json"), ReaderSnapshot.Model(model));
    var hashes = new Dictionary<string, string>();
    foreach (var format in new[] { OutputFormat.Png, OutputFormat.Svg, OutputFormat.Markdown })
    {
        using var input = File.OpenRead(path);
        using var capture = new ReaderSnapshot.Capture();
        await ExcelConverter.RenderAsync(input, new() { OutputFormat = format, ReadOptions = new() { ColumnWidthMetrics = new() { Mode = ColumnWidthMetricsMode.RenderingFontLegacy } }, FontOptions = new() { AllowSystemFonts = false, UseFontPack = false, FontFiles = [root + "/third_party/NotoSansJP/NotoSansJP-Regular.ttf"] } }, capture);
        foreach (var item in capture.Artifacts)
        {
            hashes.Add(format + "/" + item.Key, ReaderSnapshot.ArtifactHash(format, item.Value.ToArray()));
            var target = Path.Combine(output, name, format.ToString(), item.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, item.Value.ToArray());
        }
    }
    artifacts.Add(name, hashes);
    Console.WriteLine(name);
}
File.WriteAllText(Path.Combine(output, "artifacts.json"), JsonSerializer.Serialize(artifacts, new JsonSerializerOptions { WriteIndented = true }));
