using System.Globalization;
using System.Text;

namespace ExcelRenderer.Rendering;

/// <summary>Serializes conversion results using the stable manifest schema.</summary>
public static class ConversionManifest
{
    public const int SchemaVersion = 1;

    /// <summary>Writes a UTF-8 JSON manifest. The manifest contains relative artifact names only.</summary>
    public static Task WriteAsync(Stream output, ConversionResult result, CancellationToken cancellationToken = default)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (result is null) throw new ArgumentNullException(nameof(result));
        cancellationToken.ThrowIfCancellationRequested();
        var json = new StringBuilder()
            .Append("{\"schemaVersion\":").Append(result.SchemaVersion)
            .Append(",\"completionStatus\":\"").Append(Escape(result.CompletionStatus)).Append('"')
            .Append(",\"selectedSheets\":[").Append(string.Join(",", result.SelectedSheets.Select(x => "\"" + Escape(x) + "\""))).Append(']')
            .Append(",\"artifacts\":[").Append(string.Join(",", result.Artifacts.Select(Artifact))).Append(']')
            .Append(",\"diagnostics\":[").Append(string.Join(",", result.Diagnostics.Select(Diagnostic))).Append("]}").ToString();
        return WriteAsyncCore(output, json, cancellationToken);
    }

    private static async Task WriteAsyncCore(Stream output, string json, CancellationToken cancellationToken)
    {
        var bytes = new UTF8Encoding(false).GetBytes(json);
        await output.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
    }

    private static string Artifact(ArtifactMetadata artifact) =>
        "{\"artifactId\":\"" + Escape(artifact.Descriptor.ArtifactId) + "\",\"kind\":\"" + Escape(artifact.Descriptor.Kind) +
        "\",\"mediaType\":\"" + Escape(artifact.Descriptor.MediaType) + "\",\"relativeName\":\"" +
        Escape(artifact.Descriptor.RelativeName) + "\",\"byteLength\":" + artifact.ByteLength.ToString(CultureInfo.InvariantCulture) + "}";

    private static string Diagnostic(ConversionDiagnostic diagnostic) =>
        "{\"code\":\"" + Escape(diagnostic.Code) + "\",\"severity\":\"" + diagnostic.Severity +
        "\",\"stage\":\"" + diagnostic.Stage + "\",\"message\":\"" + Escape(diagnostic.Message) +
        "\",\"occurrenceCount\":" + diagnostic.OccurrenceCount.ToString(CultureInfo.InvariantCulture) + "}";

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"")
        .Replace("\r", "\\r").Replace("\n", "\\n");
}
