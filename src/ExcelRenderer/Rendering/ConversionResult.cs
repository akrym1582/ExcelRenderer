namespace ExcelRenderer.Rendering;

/// <summary>変換で利用者から観測できる結果を保持します。</summary>
/// <param name="SchemaVersion">この結果を解釈するマニフェストのスキーマバージョンです。</param>
/// <param name="CompletionStatus">変換の完了状態を表す文字列です。</param>
/// <param name="SelectedSheets">変換対象として選択されたワークシート名の一覧です。</param>
/// <param name="Pages">描画されたページの一覧です。</param>
/// <param name="Artifacts">出力された生成物の一覧です。</param>
/// <param name="Diagnostics">変換中に収集された診断の一覧です。</param>
public sealed record ConversionResult(
    int SchemaVersion,
    string CompletionStatus,
    IReadOnlyList<string> SelectedSheets,
    IReadOnlyList<RenderPageDescriptor> Pages,
    IReadOnlyList<ArtifactMetadata> Artifacts,
    IReadOnlyList<ConversionDiagnostic> Diagnostics);
