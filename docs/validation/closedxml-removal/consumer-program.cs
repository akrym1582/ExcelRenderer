using System.Reflection;
using System.Text.Json;
using ExcelRenderer;
using ExcelRenderer.Excel;
using ExcelRenderer.Rendering;
using ExcelRenderer.Mapping;
var root = "/workspace/ExcelRenderer";
var path = root + "/tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx";
var font = root + "/third_party/NotoSansJP/NotoSansJP-Regular.ttf";
var book = new ExcelReader().Read(path);
if (book.Sheets.Count == 0) throw new Exception("Missing public Reader model");
using(var source=File.OpenRead(path))
using(var reader=ExcelRenderer.OpenXml.OpenXmlWorkbookReader.Open(source))
{
    var cell=reader.EnumerateCells(1).First();
    var style=ExcelRenderer.Excel.ExcelStyleConverter.Convert(reader.ResolveStyle(cell),cell.Value.Type);
    if(style.Font.Size<=0)throw new Exception("Style API migration failed");
}
var outputs = new Dictionary<string,long>();
foreach (var format in Enum.GetValues<OutputFormat>())
{
    using var input = File.OpenRead(path);
    using var output = new CaptureSink();
    var callbacks = 0;
    var result = await ExcelConverter.RenderAsync(input, new()
    {
        OutputFormat = format,
        ReadOptions = new() { Backend = ReaderBackend.Auto, BackendSelected = value => { if (value.SelectedBackend != ReaderBackend.OpenXml) throw new Exception("Wrong reader"); callbacks++; } },
        FontOptions = new() { FontFiles = [font], AllowSystemFonts = false, UseFontPack = false },
    }, output);
    if (callbacks != 1 || output.Streams.Sum(stream => stream.Length) == 0 || result.CompletionStatus != "Completed") throw new Exception("Failed " + format);
    outputs.Add(format.ToString(), output.Streams.Sum(stream => stream.Length));
}
using (var input = File.OpenRead(path))
using (var output = new MemoryStream())
{
    var core = await ExcelRenderer.Core.ExcelConverter.ConvertAsync(input, output, new() { FontFilePath = font });
    if (core.PageCount == 0 || output.Length == 0) throw new Exception("Core conversion failed");
    outputs.Add("CorePdf", output.Length);
}
var mappingCallbacks=0;
var template=root+"/samples/mapping/invoice/template.xlsx";
using(var input=File.OpenRead(template))
using(var mapped=new MemoryStream())
{
    var json=File.ReadAllText(root+"/samples/mapping/invoice/data.json");
    ExcelTemplateMapper.MapJson(input,mapped,json,new(){Backend=MappingBackend.Auto,BackendSelected=value=>{if(value.SelectedBackend!=MappingBackend.OpenXml)throw new Exception("Wrong mapping backend");mappingCallbacks++;}});
    if(mappingCallbacks!=1)throw new Exception("Mapping callback count");
    foreach(var format in Enum.GetValues<OutputFormat>())
    {
        mapped.Position=0;using var sink=new CaptureSink();
        var result=await ExcelConverter.RenderAsync(mapped,new(){OutputFormat=format,FontOptions=new(){FontFiles=[font],AllowSystemFonts=false,UseFontPack=false}},sink);
        if(result.CompletionStatus!="Completed"||sink.Streams.Sum(stream=>stream.Length)==0)throw new Exception("Mapped output failed");
        outputs.Add("Mapped"+format,sink.Streams.Sum(stream=>stream.Length));
    }
    File.WriteAllBytes("/tmp/excelrenderer-removal-consumer/mapped.xlsx",mapped.ToArray());
}
var references = new[] { typeof(ExcelReader).Assembly, typeof(ExcelRenderer.Core.ExcelConverter).Assembly, typeof(ExcelRenderer.OpenXml.OpenXmlWorkbookReader).Assembly, typeof(ExcelTemplateMapper).Assembly }
    .ToDictionary(assembly => assembly.GetName().Name!, assembly => assembly.GetReferencedAssemblies().Select(name => name.Name).Order().ToArray());
if (references.Values.SelectMany(names => names).Any(name => name!.StartsWith("ClosedXML", StringComparison.OrdinalIgnoreCase))) throw new Exception("Legacy assembly reference");
if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name!.StartsWith("ClosedXML", StringComparison.OrdinalIgnoreCase))) throw new Exception("Legacy assembly was loaded");
var fixtureValidation = new Dictionary<string, int>();
foreach (var file in Directory.GetFiles("/tmp/excelrenderer-removal-consumer", "*.xlsx"))
{
    using var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(file, false);
    var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(document).ToArray();
    fixtureValidation.Add(Path.GetFileName(file), errors.Length);
    using var original = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(template, false);
    var originalErrors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(original).Select(error=>error.Description).ToArray();
    if (errors.Select(error=>error.Description).Except(originalErrors).Any()) throw new Exception("New schema errors: "+string.Join("; ",errors.Select(error=>error.Description)));
}
Console.WriteLine(JsonSerializer.Serialize(new { Outputs = outputs, References = references, ClosedXmlAssemblyLoaded = false, FixtureValidation = fixtureValidation }));

sealed class CaptureSink : IRenderOutputSink, IDisposable
{
    public List<MemoryStream> Streams { get; } = new();
    public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken token)
    {
        var stream = new MemoryStream(); Streams.Add(stream); return new(stream);
    }
    public ValueTask CompleteAsync(ArtifactDescriptor artifact, long length, CancellationToken token) => default;
    public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken token) => default;
    public void Dispose() { foreach (var stream in Streams) stream.Dispose(); }
}
