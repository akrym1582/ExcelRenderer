using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Abstractions;

/// <summary>
/// 描画コマンドを指定された出力ストリームへレンダリングする機能を定義します。
/// </summary>
public interface IRenderer
{
    /// <summary>
    /// 描画コマンドをページ設定に従って出力ストリームへ書き込みます。
    /// </summary>
    /// <param name="commands">ページ番号と描画内容を保持する、出力対象の描画コマンド一覧です。</param>
    /// <param name="pageSettings">出力ページの寸法、余白、および拡大縮小方法を指定する設定です。</param>
    /// <param name="output">レンダリング結果の書き込み先ストリームです。</param>
    void Render(IReadOnlyList<DrawCommand> commands, PageSettings pageSettings, Stream output);
}
