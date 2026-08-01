namespace ExcelRenderer.Model;

public sealed record BorderStyle(
    BorderSide? Left = null,
    BorderSide? Top = null,
    BorderSide? Right = null,
    BorderSide? Bottom = null);
