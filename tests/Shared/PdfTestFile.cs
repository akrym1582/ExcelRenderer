using System.Collections;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace ExcelRenderer.TestFixtures;

// Test-only view over an independent reader. No product parser is used here.
internal sealed class PdfTestFile : IDisposable
{
    private readonly PdfDocument document;
    private readonly Dictionary<IToken, PdfTestValue> cache = new();
    private readonly Dictionary<IndirectReference, PdfTestValue> references = new();

    private PdfTestFile(PdfDocument document)
    {
        this.document = document;
        Pages = document.GetPages().Select(page => new PdfTestPage(this, page)).ToArray();
        foreach (var page in Pages) cache[page.Token] = page;
        var pageIndex = 0;
        var visited = new HashSet<IndirectReference>();
        void RegisterPages(IToken token)
        {
            var reference = token as IndirectReferenceToken ?? throw new InvalidDataException("Page tree reference required");
            if (!visited.Add(reference.Data)) throw new InvalidDataException("Cyclic page tree");
            var dictionary = (DictionaryToken)document.Structure.GetObject(reference.Data).Data;
            if (dictionary.Data["Type"] is NameToken { Data: "Page" })
            {
                references[reference.Data] = Pages[pageIndex];
                cache[dictionary] = Pages[pageIndex++];
            }
            else
            {
                var kids = dictionary.Data["Kids"];
                if (kids is IndirectReferenceToken indirect) kids = document.Structure.GetObject(indirect.Data).Data;
                foreach (var child in ((ArrayToken)kids).Data) RegisterPages(child);
            }
        }
        RegisterPages(document.Structure.Catalog.CatalogDictionary.Data["Pages"]);
        if (pageIndex != Pages.Count) throw new InvalidDataException("Page tree count differs");
        Internals = new(this);
    }

    internal IReadOnlyList<PdfTestPage> Pages { get; }
    internal int PageCount => Pages.Count;
    internal PdfTestObjects Internals { get; }
    internal static PdfTestFile Open(Stream stream) => new(PdfDocument.Open(stream, new ParsingOptions { UseLenientParsing = false }));
    internal static PdfTestFile Open(string path) => new(PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = false }));
    public void Dispose() => document.Dispose();

    internal PdfTestValue Wrap(IToken token)
    {
        if (cache.TryGetValue(token, out var value)) return value;
        value = token switch
        {
            IndirectReferenceToken reference => new PdfTestReference(this, reference),
            StreamToken stream => new PdfTestMap(this, stream.StreamDictionary, stream),
            DictionaryToken dictionary => new PdfTestMap(this, dictionary),
            ArrayToken array => new PdfTestArray(this, array),
            NumericToken number => new PdfTestNumber(number.Double),
            NameToken name => new PdfTestName("/" + name.Data),
            StringToken text => new PdfTestString(text.Data),
            HexToken hex => new PdfTestString(DecodeString(hex.Bytes.ToArray())),
            BooleanToken boolean => new PdfTestBoolean(boolean.Data),
            _ => new PdfTestString(token.ToString() ?? string.Empty),
        };
        cache[token] = value;
        return value;
    }

    internal PdfTestValue Resolve(IndirectReference reference)
    {
        if (!references.TryGetValue(reference, out var value))
        {
            value = Wrap(document.Structure.GetObject(reference).Data);
            references.Add(reference, value);
        }
        return value;
    }

    private static string DecodeString(byte[] bytes) => bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255
        ? Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2) : Encoding.Latin1.GetString(bytes);

    internal sealed class PdfTestObjects(PdfTestFile file)
    {
        internal IEnumerable<PdfTestValue> GetAllObjects()
        {
            var visited = new HashSet<PdfTestValue>();
            var pending = new Stack<PdfTestValue>(file.Pages);
            while (pending.TryPop(out var item))
            {
                if (item is PdfTestReference reference) item = reference.Value;
                if (!visited.Add(item)) continue;
                yield return item;
                if (item is PdfTestMap map)
                    foreach (var child in map.Elements.Values) pending.Push(child);
                else if (item is PdfTestArray array)
                    foreach (var child in array.Elements) pending.Push(child);
            }
        }
    }
}

internal abstract class PdfTestValue;
internal sealed class PdfTestNumber(double value) : PdfTestValue
{
    internal double Value => value;
    public override string ToString() => value.ToString(CultureInfo.InvariantCulture);
}
internal sealed class PdfTestName(string value) : PdfTestValue
{
    public override string ToString() => value;
}
internal sealed class PdfTestString(string value) : PdfTestValue
{
    internal string Value => value;
    public override string ToString() => value;
}
internal sealed class PdfTestBoolean(bool value) : PdfTestValue
{
    internal bool Value => value;
}
internal sealed class PdfTestReference(PdfTestFile file, IndirectReferenceToken token) : PdfTestValue
{
    internal PdfTestValue Value => file.Resolve(token.Data);
    internal IndirectReference ObjectID => token.Data;
    public override string ToString() => token.Data.ToString();
}
internal class PdfTestMap(PdfTestFile file, DictionaryToken token, StreamToken? stream = null) : PdfTestValue
{
    internal DictionaryToken Token => token;
    internal PdfTestEntries Elements => new(file, token);
    internal PdfTestStream Stream => stream is null ? throw new InvalidDataException("Expected a stream") : new(stream);
}
internal sealed class PdfTestEntries(PdfTestFile file, DictionaryToken token)
{
    internal IEnumerable<string> Keys => token.Data.Keys.Select(key => "/" + key);
    internal IEnumerable<PdfTestValue> Values => token.Data.Values.Select(file.Wrap);
    internal bool ContainsKey(string key) => token.Data.ContainsKey(key.TrimStart('/'));
    private PdfTestValue? Get(string key)
    {
        if (!token.Data.TryGetValue(key.TrimStart('/'), out var item)) return null;
        var value = file.Wrap(item);
        return value is PdfTestReference reference ? reference.Value : value;
    }
    internal PdfTestMap? GetDictionary(string key) => Get(key) as PdfTestMap;
    internal PdfTestArray? GetArray(string key) => Get(key) as PdfTestArray;
    internal string GetName(string key) => Get(key) is PdfTestName name ? name.ToString() : string.Empty;
    internal string GetString(string key) => (Get(key) as PdfTestString)?.Value ?? string.Empty;
    internal double GetReal(string key) => (Get(key) as PdfTestNumber)?.Value ?? 0;
    internal bool GetBoolean(string key) => (Get(key) as PdfTestBoolean)?.Value ?? false;
}
internal sealed class PdfTestArray(PdfTestFile file, ArrayToken token) : PdfTestValue
{
    internal PdfTestArrayEntries Elements => new(token.Data.Select(file.Wrap).ToArray());
    public override string ToString() => "[ " + string.Join(" ", Elements) + " ]";
}
internal sealed class PdfTestArrayEntries(PdfTestValue[] values) : IReadOnlyList<PdfTestValue>
{
    public int Count => values.Length;
    public PdfTestValue this[int index] => values[index];
    public IEnumerator<PdfTestValue> GetEnumerator() => ((IEnumerable<PdfTestValue>)values).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    internal PdfTestMap? GetDictionary(int index) => (values[index] is PdfTestReference reference ? reference.Value : values[index]) as PdfTestMap;
}
internal sealed class PdfTestStream(StreamToken stream)
{
    internal byte[] Value => stream.Data.ToArray();
    internal byte[] UnfilteredValue
    {
        get
        {
            if (!stream.StreamDictionary.Data.TryGetValue("Filter", out var filter)) return Value;
            if (filter is not NameToken { Data: "FlateDecode" }) throw new InvalidDataException("Unsupported test stream filter: " + filter);
            using var compressed = new MemoryStream(Value);
            using var inflate = new ZLibStream(compressed, CompressionMode.Decompress);
            using var output = new MemoryStream();
            inflate.CopyTo(output);
            return output.ToArray();
        }
    }
}
internal sealed class PdfTestPage(PdfTestFile file, UglyToad.PdfPig.Content.Page page) : PdfTestMap(file, page.Dictionary)
{
    internal TestPoints Width => new(page.Width);
    internal TestPoints Height => new(page.Height);
    internal TestBox MediaBox => Box("/MediaBox");
    internal TestBox CropBox => Elements.GetArray("/CropBox") is null ? MediaBox : Box("/CropBox");
    internal IReadOnlyList<PdfTestMap> Annotations => Elements.GetArray("/Annots")?.Elements.Select(value => (PdfTestMap)(value is PdfTestReference reference ? reference.Value : value)).ToArray() ?? [];
    internal IReadOnlyList<UglyToad.PdfPig.Graphics.Operations.IGraphicsStateOperation> Operations => page.Operations;
    internal string Text => page.Text;
    internal IEnumerable<string> ActualTexts => page.Operations
        .OfType<UglyToad.PdfPig.Graphics.Operations.MarkedContent.BeginMarkedContentWithProperties>()
        .Where(operation => operation.Properties is not null && operation.Properties.Data.ContainsKey("ActualText"))
        .Select(operation => operation.Properties!.Data["ActualText"] switch
        {
            HexToken hex => Decode(hex.Bytes.ToArray()),
            StringToken text => text.Data,
            _ => throw new InvalidDataException("ActualText must be a string"),
        });
    private static string Decode(byte[] bytes) => bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255
        ? Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2) : Encoding.Latin1.GetString(bytes);
    private TestBox Box(string name)
    {
        var array = Elements.GetArray(name)?.Elements;
        return array is null ? new(0, 0, page.Width, page.Height) : new(((PdfTestNumber)array[0]).Value, ((PdfTestNumber)array[1]).Value, ((PdfTestNumber)array[2]).Value, ((PdfTestNumber)array[3]).Value);
    }
}
internal readonly record struct TestPoints(double Point);
internal readonly record struct TestBox(double X1, double Y1, double X2, double Y2)
{
    internal double Width => X2 - X1;
    internal double Height => Y2 - Y1;
}
