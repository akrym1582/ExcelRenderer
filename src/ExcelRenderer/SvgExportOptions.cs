using ExcelRenderer.Fonts;

namespace ExcelRenderer;

/// <summary>SVG 出力の対象ワークシートを指定します。</summary>
public sealed class SvgExportOptions
{
    /// <summary>Gets the workbook formula display policy.</summary>
    public ExcelRenderer.Excel.ExcelReadOptions ReadOptions { get; init; } = new();

    /// <summary>Gets the SVG intermediate buffer policy; it does not affect PDF or PNG buffers.</summary>
    public ExcelRenderer.Rendering.RenderBufferOptions Buffering { get; init; } = new();

    /// <summary>Gets one worksheet name. Use SheetNames for multiple sheets; leave both null to export all worksheets.</summary>
    public string? SheetName { get; init; }

    /// <summary>Gets the worksheet names in output order, with duplicates removed. Null uses SheetName or all worksheets. Cannot be combined with SheetName.</summary>
    public IReadOnlyList<string>? SheetNames { get; init; }

    /// <summary>Gets the font selection criteria. 描画に使用するフォントの検索設定を取得します。</summary>
    public FontOptions FontOptions { get; init; } = new();
}
