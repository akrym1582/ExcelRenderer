using ExcelRenderer.Fonts;

namespace ExcelRenderer.Rendering;

/// <summary>ストリームを入力として実行する変換要求を記述します。</summary>
public sealed record RenderRequest
{
    /// <summary>Gets the output format to generate. 生成する出力形式です。</summary>
    public OutputFormat OutputFormat { get; init; }

    /// <summary>Gets the worksheet and page selection criteria. ワークシートとページの選択条件です。</summary>
    public SelectionOptions Selection { get; init; } = new();

    /// <summary>Gets the diagnostic collection and failure criteria. 診断の収集と失敗判定の条件です。</summary>
    public DiagnosticOptions DiagnosticOptions { get; init; } = new();

    /// <summary>Gets the input-size and ZIP-expansion criteria. 入力ストリームのサイズと ZIP 展開に関する条件です。</summary>
    public WorkbookInputOptions Input { get; init; } = new();

    /// <summary>Gets the font selection criteria for PDF, PNG, and SVG rendering. PDF、PNG、および SVG の描画に使用するフォントの選択条件です。</summary>
    public FontOptions FontOptions { get; init; } = new();

    /// <summary>Gets the PNG output resolution in DPI. PNG 出力の解像度を DPI で指定します。</summary>
    public double Dpi { get; init; } = 96;
}
