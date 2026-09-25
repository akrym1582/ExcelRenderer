namespace ExcelRenderer.Model;

/// <summary>
/// BorderStyle が表すデータと操作を提供します.
/// </summary>
public sealed record BorderStyle(
    BorderSide? Left = null,
    BorderSide? Top = null,
    BorderSide? Right = null,
    BorderSide? Bottom = null);
