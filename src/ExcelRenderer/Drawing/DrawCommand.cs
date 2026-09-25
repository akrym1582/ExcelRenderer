using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// 特定のページへ出力する描画操作の基底データを表します。
/// </summary>
public abstract record DrawCommand(int PageNumber);
