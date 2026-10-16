using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.TestFixtures;

internal static class ReaderSnapshot
{
    internal sealed class InvariantCultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
        private readonly System.Globalization.CultureInfo previousUi = System.Globalization.CultureInfo.CurrentUICulture;
        internal InvariantCultureScope()
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
        }
        public void Dispose()
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
            System.Globalization.CultureInfo.CurrentUICulture = previousUi;
        }
    }

    internal static string Model(ReportDocument document) => JsonSerializer.Serialize(document.Sheets.Select(sheet => new
    {
        sheet.Name,
        Cells = sheet.Cells.OrderBy(pair => pair.Key.Row).ThenBy(pair => pair.Key.Column).Select(pair => new
        {
            Address = pair.Key.ToString(), pair.Value.Text, pair.Value.Style, pair.Value.RowSpan, pair.Value.ColumnSpan,
            pair.Value.Formula, pair.Value.FormulaSharedIndex, pair.Value.FormulaMasterAddress, pair.Value.MergedBorders,
        }),
        sheet.MergedRanges, sheet.PrintAreas, sheet.PageSettings, sheet.DefaultColumnWidth, sheet.DefaultRowHeight,
        Columns = sheet.Columns.Where(pair => pair.Value != new ColumnDefinition(sheet.DefaultColumnWidth)).OrderBy(pair => pair.Key).Select(pair => new { Index = pair.Key, Definition = pair.Value }),
        Rows = sheet.Rows.Where(pair => pair.Value != new RowDefinition(sheet.DefaultRowHeight)).OrderBy(pair => pair.Key).Select(pair => new { Index = pair.Key, Definition = pair.Value }),
        Images = sheet.Images?.Select(image => new { image.Anchor, image.OffsetX, image.OffsetY, image.Width, image.Height, Hash = Hash(image.ImageBytes) }),
    }));

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    internal static string ArtifactHash(OutputFormat format, byte[] bytes) => Hash(format == OutputFormat.Svg ? Encoding.UTF8.GetBytes(CanonicalSvg(bytes)) : bytes);

    internal static string CanonicalSvg(byte[] bytes)
    {
        var document = XDocument.Parse(Encoding.UTF8.GetString(bytes));
        var ids = document.Root!.DescendantsAndSelf().Attributes("id").Select((attribute, index) => (attribute.Value, Name: "resource" + index))
            .ToDictionary(item => item.Value, item => item.Name, StringComparer.Ordinal);
        foreach (var attribute in document.Root.DescendantsAndSelf().Attributes())
        {
            if (attribute.Name.LocalName == "id") attribute.Value = ids[attribute.Value];
            else if (attribute.Value.StartsWith("url(#", StringComparison.Ordinal) && attribute.Value.EndsWith(")", StringComparison.Ordinal) && ids.TryGetValue(attribute.Value.Substring(5, attribute.Value.Length - 6), out var referenced)) attribute.Value = "url(#" + referenced + ")";
            else if (attribute.Value.StartsWith("#", StringComparison.Ordinal) && ids.TryGetValue(attribute.Value.Substring(1), out referenced)) attribute.Value = "#" + referenced;
        }
        return document.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    internal sealed class Capture : IRenderOutputSink, IDisposable
    {
        internal Dictionary<string, MemoryStream> Artifacts { get; } = new();
        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken)
        {
            var stream = new MemoryStream();
            Artifacts.Add(artifact.RelativeName, stream);
            return new(stream);
        }
        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) => default;
        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) => default;
        public void Dispose() { foreach (var stream in Artifacts.Values) stream.Dispose(); }
    }
}
