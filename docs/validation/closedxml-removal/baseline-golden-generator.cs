using System.Text.Json;
using ExcelRenderer;
using ExcelRenderer.Excel;
using ExcelRenderer.Rendering;
using ExcelRenderer.TestFixtures;
using var culture = new ReaderSnapshot.InvariantCultureScope();
var root = Path.GetFullPath(args[0]);
var paths = Directory.EnumerateFiles(root + "/samples", "*.xlsx", SearchOption.AllDirectories).Concat(Directory.EnumerateFiles(root + "/tests/ExcelRenderer.Tests/SampleInputs", "*.xlsx", SearchOption.AllDirectories)).Order();
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var artifacts = new Dictionary<string, Dictionary<string, string>>();
foreach (var path in paths)
{
    var name = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    var key = ReaderSnapshot.Hash(System.Text.Encoding.UTF8.GetBytes(name));
    var model = new ExcelReader().Read(path);
    File.WriteAllText(Path.Combine(output, key + ".json"), ReaderSnapshot.Model(model));
    var hashes = new Dictionary<string, string>();
    foreach (var format in new[] { OutputFormat.Png, OutputFormat.Svg, OutputFormat.Markdown })
    {
        using var input = File.OpenRead(path);
        using var capture = new ReaderSnapshot.Capture();
        await ExcelConverter.RenderAsync(input, new() { OutputFormat = format, FontOptions = new() { AllowSystemFonts = false, UseFontPack = false, FontFiles = [root + "/third_party/NotoSansJP/NotoSansJP-Regular.ttf"] } }, capture);
        foreach (var item in capture.Artifacts) hashes.Add(format + "/" + item.Key, ReaderSnapshot.ArtifactHash(format, item.Value.ToArray()));
    }
    artifacts.Add(name, hashes);
    Console.WriteLine(name);
}
File.WriteAllText(Path.Combine(output, "artifacts.json"), JsonSerializer.Serialize(artifacts, new JsonSerializerOptions { WriteIndented = true }));
