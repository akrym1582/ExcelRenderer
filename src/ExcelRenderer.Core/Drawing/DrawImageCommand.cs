using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Drawing;

/// <summary>
/// 指定した矩形内へバイナリ画像を描画するコマンドを表します。
/// </summary>
internal sealed record DrawImageCommand(int PageNumber, ReportRect Bounds, byte[] ImageBytes) : DrawCommand(PageNumber);
