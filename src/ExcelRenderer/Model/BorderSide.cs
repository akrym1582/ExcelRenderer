namespace ExcelRenderer.Model;

/// <summary>
/// BorderSide が表すデータと操作を提供します.
/// </summary>
public sealed record BorderSide(double Width = 0.5, ReportColor? Color = null,
    BorderLineStyle LineStyle = BorderLineStyle.Solid);
