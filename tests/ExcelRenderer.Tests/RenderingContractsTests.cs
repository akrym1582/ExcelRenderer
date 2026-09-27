using ExcelRenderer.Rendering;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>Tests ownership and serialization guarantees of the stream rendering contracts.</summary>
public sealed class RenderingContractsTests
{
    /// <summary>Completing a single-stream sink does not close the caller-owned stream.</summary>
    [Fact]
    public async Task Single_stream_sink_leaves_caller_stream_open()
    {
        using var output = new MemoryStream();
        var sink = new SingleStreamOutputSink(output);
        var artifact = new ArtifactDescriptor("pdf", "pdf", "application/pdf", "workbook.pdf");

        var stream = await sink.OpenAsync(artifact, CancellationToken.None);
        await stream.WriteAsync(new byte[] { 1, 2, 3 }, 0, 3);
        await sink.CompleteAsync(artifact, 3, CancellationToken.None);

        Assert.True(output.CanWrite);
        Assert.Equal(3, output.Length);
    }

    /// <summary>The manifest uses relative artifact names and valid escaped JSON strings.</summary>
    [Fact]
    public async Task Manifest_writes_relative_artifact_metadata()
    {
        var result = new ConversionResult(
            ConversionManifest.SchemaVersion,
            "Completed",
            new[] { "Sheet" },
            Array.Empty<RenderPageDescriptor>(),
            new[] { new ArtifactMetadata(new("png-1", "png", "image/png", "Sheet-1.png"), 3) },
            new[] { new ConversionDiagnostic("UnsupportedShape", DiagnosticSeverity.Warning, DiagnosticStage.Read, "quoted \"message\"") });
        using var output = new MemoryStream();

        await ConversionManifest.WriteAsync(output, result);

        var json = System.Text.Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("\"relativeName\":\"Sheet-1.png\"", json);
        Assert.Contains("quoted \\\"message\\\"", json);
    }
}
