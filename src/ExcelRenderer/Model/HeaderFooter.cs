namespace ExcelRenderer.Model;

public sealed record HeaderFooter(
    HeaderFooterSection Header,
    HeaderFooterSection Footer,
    HeaderFooterSection? FirstPageHeader = null,
    HeaderFooterSection? FirstPageFooter = null,
    HeaderFooterSection? EvenPageHeader = null,
    HeaderFooterSection? EvenPageFooter = null);
