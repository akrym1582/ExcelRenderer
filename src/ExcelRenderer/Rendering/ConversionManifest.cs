using System.Globalization;
using System.Text;

namespace ExcelRenderer.Rendering;

/// <summary>安定したマニフェストスキーマを使用して変換結果を直列化します。</summary>
public static class ConversionManifest
{
    /// <summary>このライブラリが生成するマニフェストのスキーマバージョンです。</summary>
    public const int SchemaVersion = 1;

    /// <summary>UTF-8 の JSON マニフェストを書き込みます。</summary>
    /// <remarks>マニフェストには生成物の相対名だけが含まれるため、出力先の絶対パスを漏らしません。</remarks>
    /// <param name="output">マニフェストを書き込むストリームです。呼び出し元が所有し、メソッドは閉じません。</param>
    /// <param name="result">マニフェストへ変換する結果です。</param>
    /// <param name="cancellationToken">書き込みのキャンセルを通知するトークンです。</param>
    /// <returns>JSON の書き込みが完了したときに完了するタスクを返します。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> または <paramref name="result"/> が <see langword="null"/> の場合にスローされます。</exception>
    /// <exception cref="OperationCanceledException">書き込みがキャンセルされた場合にスローされます。</exception>
    public static Task WriteAsync(Stream output, ConversionResult result, CancellationToken cancellationToken = default)
    {
        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var json = new StringBuilder()
            .Append("{\"schemaVersion\":").Append(result.SchemaVersion)
            .Append(",\"completionStatus\":\"").Append(Escape(result.CompletionStatus)).Append('"')
            .Append(",\"selectedSheets\":[").Append(string.Join(",", result.SelectedSheets.Select(x => "\"" + Escape(x) + "\""))).Append(']')
            .Append(",\"artifacts\":[").Append(string.Join(",", result.Artifacts.Select(Artifact))).Append(']')
            .Append(",\"pages\":[").Append(string.Join(",", result.Pages.Select(Page))).Append(']')
            .Append(",\"diagnostics\":[").Append(string.Join(",", result.Diagnostics.Select(Diagnostic))).Append("]}").ToString();
        return WriteAsyncCore(output, json, cancellationToken);
    }

    private static async Task WriteAsyncCore(Stream output, string json, CancellationToken cancellationToken)
    {
        var bytes = new UTF8Encoding(false).GetBytes(json);
        await output.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
    }

    private static string Page(RenderPageDescriptor page)
    {
        var metadata = new ArtifactDescriptor(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            WidthPoints: page.WidthPoints,
            HeightPoints: page.HeightPoints,
            PixelWidth: page.PixelWidth,
            PixelHeight: page.PixelHeight)
        {
            RequestedRange = page.RequestedRange,
            OriginalWidthPoints = page.OriginalWidthPoints,
            OriginalHeightPoints = page.OriginalHeightPoints,
            CropBounds = page.CropBounds,
            PaddingPoints = page.PaddingPoints,
        };
        return "{\"sourceSheetIndex\":" + page.SourceSheetIndex + ",\"sourceSheetName\":\"" + Escape(page.SourceSheetName) +
            "\",\"sourcePageNumber\":" + page.SourcePageNumber + ",\"documentPageNumber\":" + page.DocumentPageNumber +
            ",\"outputPageNumber\":" + (page.OutputPageNumber?.ToString(CultureInfo.InvariantCulture) ?? "null") + OptionalArtifactMetadata(metadata) +
            (page.SourceCellRanges is { Count: > 0 } regions ? ",\"sourceCellRanges\":[" + string.Join(",", regions.Select(r => "\"" + Markdown.MarkdownExporter.Range(r) + "\"")) + "]" : string.Empty) + "}";
    }

    private static string Artifact(ArtifactMetadata artifact) =>
        "{\"artifactId\":\"" + Escape(artifact.Descriptor.ArtifactId) + "\",\"kind\":\"" + Escape(artifact.Descriptor.Kind) +
        "\",\"mediaType\":\"" + Escape(artifact.Descriptor.MediaType) + "\",\"relativeName\":\"" +
    Escape(artifact.Descriptor.RelativeName) + "\",\"byteLength\":" + artifact.ByteLength.ToString(CultureInfo.InvariantCulture) +
    OptionalArtifactMetadata(artifact.Descriptor) + "}";

    private static string OptionalArtifactMetadata(ArtifactDescriptor descriptor)
    {
        var properties = new List<string>();
        if (descriptor.IsContinuous)
        {
            properties.Add("\"imageLayout\":\"continuous\"");
        }

        if (descriptor.SourceSheetName is not null)
        {
            properties.Add("\"sourceSheetName\":\"" + Escape(descriptor.SourceSheetName) + "\"");
        }

        if (descriptor.WidthPoints is { } width)
        {
            properties.Add("\"widthPoints\":" + width.ToString("R", CultureInfo.InvariantCulture));
        }

        if (descriptor.HeightPoints is { } height)
        {
            properties.Add("\"heightPoints\":" + height.ToString("R", CultureInfo.InvariantCulture));
        }

        if (descriptor.PixelWidth is { } pixelWidth)
        {
            properties.Add("\"pixelWidth\":" + pixelWidth.ToString(CultureInfo.InvariantCulture));
        }

        if (descriptor.PixelHeight is { } pixelHeight)
        {
            properties.Add("\"pixelHeight\":" + pixelHeight.ToString(CultureInfo.InvariantCulture));
        }

        if (descriptor.RequestedRange is { } range)
        {
            properties.Add("\"requestedRange\":\"" + Markdown.MarkdownExporter.Range(range) + "\"");
        }

        if (descriptor.OriginalWidthPoints is { } originalWidth)
        {
            properties.Add("\"originalWidthPoints\":" + originalWidth.ToString("R", CultureInfo.InvariantCulture));
            properties.Add("\"originalHeightPoints\":" + descriptor.OriginalHeightPoints!.Value.ToString("R", CultureInfo.InvariantCulture));
        }

        if (descriptor.CropBounds is { } crop)
        {
            properties.Add("\"cropBounds\":{\"x\":" + crop.X.ToString("R", CultureInfo.InvariantCulture) + ",\"y\":" + crop.Y.ToString("R", CultureInfo.InvariantCulture) +
                ",\"width\":" + crop.Width.ToString("R", CultureInfo.InvariantCulture) + ",\"height\":" + crop.Height.ToString("R", CultureInfo.InvariantCulture) + "}");
            properties.Add("\"paddingPoints\":" + descriptor.PaddingPoints!.Value.ToString("R", CultureInfo.InvariantCulture));
        }

        return properties.Count == 0 ? string.Empty : "," + string.Join(",", properties);
    }

    private static string Diagnostic(ConversionDiagnostic diagnostic) =>
        "{\"code\":\"" + Escape(diagnostic.Code) + "\",\"severity\":\"" + diagnostic.Severity +
        "\",\"stage\":\"" + diagnostic.Stage + "\",\"message\":\"" + Escape(diagnostic.Message) +
        "\",\"occurrenceCount\":" + diagnostic.OccurrenceCount.ToString(CultureInfo.InvariantCulture) + "}";

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"")
        .Replace("\r", "\\r").Replace("\n", "\\n");
}
