namespace ReportEngine.Model;

public sealed record ReportDocument(IReadOnlyList<ReportSheet> Sheets);

public sealed record ReportSheet(
    string Name,
    IReadOnlyDictionary<CellAddress, ReportCell> Cells,
    IReadOnlyDictionary<int, ColumnDefinition> Columns,
    IReadOnlyDictionary<int, RowDefinition> Rows,
    IReadOnlyList<CellRange> MergedRanges,
    PageSettings PageSettings,
    CellRange? PrintArea = null);

public readonly record struct CellAddress(int Row, int Column);
public readonly record struct CellRange(CellAddress First, CellAddress Last)
{
    public bool Contains(CellAddress address) =>
        address.Row >= First.Row && address.Row <= Last.Row &&
        address.Column >= First.Column && address.Column <= Last.Column;
}

public sealed record ReportCell(string? Text, CellStyle Style, int RowSpan = 1, int ColumnSpan = 1);
public sealed record ColumnDefinition(double Width = 64, bool IsHidden = false);
public sealed record RowDefinition(double Height = 15, bool IsHidden = false);

public sealed record CellStyle(
    FontStyle Font,
    ReportColor? Background = null,
    BorderStyle? Border = null,
    HorizontalAlignment HorizontalAlignment = HorizontalAlignment.Left,
    VerticalAlignment VerticalAlignment = VerticalAlignment.Top,
    bool WrapText = false)
{
    public static CellStyle Default { get; } = new(new FontStyle());
}

public sealed record FontStyle(
    string Family = "Noto Sans JP",
    double Size = 10,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    ReportColor? Color = null);

public sealed record BorderStyle(
    BorderSide? Left = null,
    BorderSide? Top = null,
    BorderSide? Right = null,
    BorderSide? Bottom = null);

public sealed record BorderSide(double Width = 0.5, ReportColor? Color = null);
public readonly record struct ReportColor(byte Red, byte Green, byte Blue, byte Alpha = 255);
public enum HorizontalAlignment { Left, Center, Right }
public enum VerticalAlignment { Top, Center, Bottom }

public sealed record PageSettings(
    double Width = 595.276,
    double Height = 841.89,
    double MarginLeft = 36,
    double MarginTop = 36,
    double MarginRight = 36,
    double MarginBottom = 36);
