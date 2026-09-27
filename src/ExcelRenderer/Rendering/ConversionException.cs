namespace ExcelRenderer.Rendering;

/// <summary>診断または生成物が作成された後に変換を完了できない場合にスローされる例外です。</summary>
public sealed class ConversionException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ConversionException"/> class. 指定した失敗情報を持つ例外を初期化します。</summary>
    /// <param name="message">失敗の説明文です。</param>
    /// <param name="innerException">失敗の原因となった例外です。原因がない場合は <see langword="null"/> です。</param>
    /// <param name="diagnostics">変換中に収集された診断の一覧です。</param>
    /// <param name="artifacts">失敗前に完了した生成物の一覧です。</param>
    public ConversionException(string message, Exception? innerException, IReadOnlyList<ConversionDiagnostic> diagnostics, IReadOnlyList<ArtifactMetadata> artifacts)
        : base(message, innerException)
    {
        CollectedDiagnostics = diagnostics;
        CompletedArtifacts = artifacts;
    }

    /// <summary>Gets the diagnostics collected during conversion. 変換中に収集された診断の一覧を取得します。</summary>
    public IReadOnlyList<ConversionDiagnostic> CollectedDiagnostics { get; }

    /// <summary>Gets the artifacts completed before conversion failed. 変換失敗前に完了した生成物の一覧を取得します。</summary>
    public IReadOnlyList<ArtifactMetadata> CompletedArtifacts { get; }
}
