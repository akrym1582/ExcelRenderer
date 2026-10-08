using ExcelRenderer.Slim.Layout;
using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Drawing;

/// <summary>
/// 特定のページへ出力する描画操作の基底データを表します。
/// </summary>
internal abstract record DrawCommand(int PageNumber);
