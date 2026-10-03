using ExcelRenderer.Model;

namespace ExcelRenderer.Excel;

/// <summary>
/// xlsx に保存されたページ倍率属性を表します。
/// </summary>
/// <param name="FitToPage">ページ数に合わせるモードが有効かどうかを示します。</param>
/// <param name="Scale">明示倍率のパーセント値です。</param>
/// <param name="FitToWidth">横方向のページ数です。</param>
/// <param name="FitToHeight">縦方向のページ数です。</param>
/// <param name="DefaultColumnWidth">raw 既定列幅です。</param>
/// <param name="DefaultRowHeight">ポイント単位の既定行高です。</param>
/// <param name="Columns">raw 列定義区間です。</param>
/// <param name="NormalFont">Normal セルスタイルが参照するフォントです。</param>
/// <param name="RowBreaks">改ページ後の行番号です。</param>
/// <param name="ColumnBreaks">改ページ後の列番号です。</param>
/// <param name="PageOrder">ページを出力する順序です。</param>
/// <param name="BaseColumnWidth">Padding-free default character count.</param>
/// <param name="HorizontalCentered">Whether print content is centered horizontally.</param>
/// <param name="VerticalCentered">Whether print content is centered vertically.</param>
internal sealed record SheetPageSetupMetadata(
    bool FitToPage,
    uint? Scale,
    uint? FitToWidth,
    uint? FitToHeight,
    double? DefaultColumnWidth,
    double DefaultRowHeight,
    IReadOnlyList<RawColumnDefinition> Columns,
    NormalFontMetadata? NormalFont,
    IReadOnlyList<int> RowBreaks,
    IReadOnlyList<int> ColumnBreaks,
    PrintPageOrder PageOrder,
    double? BaseColumnWidth,
    bool HorizontalCentered,
    bool VerticalCentered);
