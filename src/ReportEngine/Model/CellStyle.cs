namespace ReportEngine.Model;

public sealed record CellStyle(
    FontStyle Font,
    ReportColor? Background = null,
    BorderStyle? Border = null,
    HorizontalAlignment HorizontalAlignment = HorizontalAlignment.Left,
    VerticalAlignment VerticalAlignment = VerticalAlignment.Top,
    bool WrapText = false,
    bool ShrinkToFit = false)
{
    public static CellStyle Default { get; } = new(new FontStyle());
}
