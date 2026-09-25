namespace ExcelRenderer.Model;

/// <summary>
/// HeaderFooter が表すデータと操作を提供します.
/// </summary>
public sealed record HeaderFooter(
    HeaderFooterSection Header,
    HeaderFooterSection Footer,
    HeaderFooterSection? FirstPageHeader = null,
    HeaderFooterSection? FirstPageFooter = null,
    HeaderFooterSection? EvenPageHeader = null,
    HeaderFooterSection? EvenPageFooter = null);
