namespace ExcelRenderer.Core.Layout;

/// <summary>Supplies a single page's geometry to coarse product content hooks.</summary>
/// <param name="Layout">The page-local cell and sheet geometry.</param>
/// <param name="Placement">The common sheet-to-page coordinate transform.</param>
/// <param name="Horizontal">The half-open horizontal band.</param>
/// <param name="Vertical">The half-open vertical band.</param>
/// <param name="BodyColumns">The body columns.</param>
/// <param name="BodyRows">The body rows.</param>
/// <param name="TitleColumns">The repeated columns.</param>
/// <param name="TitleRows">The repeated rows.</param>
/// <param name="RepeatColumns">Whether columns repeat on this page.</param>
/// <param name="RepeatRows">Whether rows repeat on this page.</param>
internal sealed record CorePageBuildContext(
    ReportLayoutContext Layout,
    PagePlacement Placement,
    PageBand Horizontal,
    PageBand Vertical,
    IReadOnlyList<int> BodyColumns,
    IReadOnlyList<int> BodyRows,
    IReadOnlyList<int> TitleColumns,
    IReadOnlyList<int> TitleRows,
    bool RepeatColumns,
    bool RepeatRows);
