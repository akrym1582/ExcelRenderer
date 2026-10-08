namespace ExcelRenderer.Slim.Model;

/// <summary>
/// 通常ページ、先頭ページ、および偶数ページに使用するヘッダーとフッターを表します。
/// </summary>
internal sealed record HeaderFooter(
    HeaderFooterSection Header,
    HeaderFooterSection Footer,
    HeaderFooterSection? FirstPageHeader = null,
    HeaderFooterSection? FirstPageFooter = null,
    HeaderFooterSection? EvenPageHeader = null,
    HeaderFooterSection? EvenPageFooter = null);
