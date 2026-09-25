using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// ワークシート内で検出した領域の内容種別を表します。
/// </summary>
public enum RegionType
{
    /// <summary>
    /// 内容種別を判定できない領域を表します。
    /// </summary>
    Unknown,

    /// <summary>
    /// 見出しとして扱う領域を表します。
    /// </summary>
    Title,

    /// <summary>
    /// 項目名と値の組で構成されるフォーム領域を表します。
    /// </summary>
    Form,

    /// <summary>
    /// 行と列がそろった表形式の領域を表します。
    /// </summary>
    Table,

    /// <summary>
    /// 単一の文章または文字列を含む領域を表します。
    /// </summary>
    Text,

    /// <summary>
    /// 画像を主体とする領域を表します。
    /// </summary>
    Image,

    /// <summary>
    /// 定型的な配置に分類できない領域を表します。
    /// </summary>
    FreeLayout,
}
