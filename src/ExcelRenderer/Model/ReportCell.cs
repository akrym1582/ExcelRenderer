namespace ExcelRenderer.Model;

public sealed record ReportCell(
    string? Text,
    CellStyle Style,
    int RowSpan = 1,
    int ColumnSpan = 1,
    string? Formula = null);
