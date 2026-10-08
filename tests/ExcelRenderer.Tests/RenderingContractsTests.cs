using ExcelRenderer.Rendering;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>ストリーム出力の所有権と、マニフェストの相対名・JSON エスケープを検証します。</summary>
public sealed class RenderingContractsTests
{
    /// <summary>単一ストリーム出力の完了後も呼び出し元のストリームが開いたままで、書き込んだ 3 バイトが保持されることを検証します。</summary>
    /// <returns>変換処理と出力結果の検証が完了するまでの非同期処理。</returns>
    [Fact(DisplayName = "単一ストリーム出力の完了後も呼び出し元のストリームが開いたままで、書き込んだ 3 バイトが保持される")]
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

    /// <summary>マニフェストに成果物の相対ファイル名を記録し、診断メッセージ内の引用符を JSON 用にエスケープすることを検証します。</summary>
    /// <returns>変換処理と出力結果の検証が完了するまでの非同期処理。</returns>
    [Fact(DisplayName = "マニフェストに成果物の相対ファイル名を記録し、診断メッセージ内の引用符を JSON 用にエスケープする")]
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
