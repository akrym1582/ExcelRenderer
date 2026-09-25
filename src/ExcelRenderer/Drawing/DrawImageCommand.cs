using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// 指定した矩形内へバイナリ画像を描画するコマンドを表します。
/// </summary>
public sealed record DrawImageCommand(int PageNumber, ReportRect Bounds, byte[] ImageBytes) : DrawCommand(PageNumber);
