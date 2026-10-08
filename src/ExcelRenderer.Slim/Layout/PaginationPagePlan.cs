namespace ExcelRenderer.Slim.Layout;

/// <summary>Represents pagination page plan data within a conversion.</summary>
/// <param name="Horizontal">The Horizontal used by this operation.</param>
/// <param name="Vertical">The Vertical used by this operation.</param>
/// <param name="BodyColumns">The BodyColumns used by this operation.</param>
/// <param name="BodyRows">The BodyRows used by this operation.</param>
/// <param name="TitleColumns">The TitleColumns used by this operation.</param>
/// <param name="TitleRows">The TitleRows used by this operation.</param>
/// <param name="TitleColumnEnd">The TitleColumnEnd used by this operation.</param>
/// <param name="TitleRowEnd">The TitleRowEnd used by this operation.</param>
/// <param name="TitleWidth">The TitleWidth used by this operation.</param>
/// <param name="TitleHeight">The TitleHeight used by this operation.</param>
/// <param name="Scale">The Scale used by this operation.</param>
/// <returns>The planned or generated result.</returns>
internal sealed record PaginationPagePlan(
    PageBand? Horizontal,
    PageBand? Vertical,
    IReadOnlyList<int> BodyColumns,
    IReadOnlyList<int> BodyRows,
    IReadOnlyList<int> TitleColumns,
    IReadOnlyList<int> TitleRows,
    double TitleColumnEnd,
    double TitleRowEnd,
    double TitleWidth,
    double TitleHeight,
    double Scale);
