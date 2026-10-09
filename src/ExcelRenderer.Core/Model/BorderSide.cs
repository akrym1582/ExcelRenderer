namespace ExcelRenderer.Core.Model;

/// <summary>
/// 罫線の太さ、色、および線種を表します。
/// </summary>
internal sealed record BorderSide(double Width = 0.5, ReportColor? Color = null,
    BorderLineStyle LineStyle = BorderLineStyle.Solid);
