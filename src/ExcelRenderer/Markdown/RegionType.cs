using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// RegionType が表すデータと操作を提供します.
/// </summary>
public enum RegionType
{
    /// <summary>
    /// この選択肢が表す状態を示します.
    /// </summary>
    Unknown,

    /// <summary>
    /// この選択肢が表す状態を示します.
    /// </summary>
    Title,

    /// <summary>
    /// この選択肢が表す状態を示します.
    /// </summary>
    Form,

    /// <summary>
    /// この選択肢が表す状態を示します.
    /// </summary>
    Table,

    /// <summary>
    /// この選択肢が表す状態を示します.
    /// </summary>
    Text,

    /// <summary>
    /// この選択肢が表す状態を示します.
    /// </summary>
    Image,

    /// <summary>
    /// この選択肢が表す状態を示します.
    /// </summary>
    FreeLayout,
}
