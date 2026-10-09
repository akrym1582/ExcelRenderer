using ClosedXML.Excel;
using ExcelRenderer.Model;

namespace ExcelRenderer.Excel;

/// <summary>ClosedXML のセル書式をレンダリング用の書式モデルへ変換します。</summary>
public static class ExcelStyleConverter
{
    /// <summary>セルのフォント、塗りつぶし、罫線、配置、および文字表示設定をレンダリング用書式へ変換します。</summary>
    /// <param name="cell">書式とデータ型を読み取る ClosedXML のセルです。</param>
    /// <returns>テーマ色を実色へ解決し、標準配置をセルのデータ型に応じて確定したセル書式を返します。</returns>
    public static CellStyle Convert(IXLCell cell) =>
        CoreIntegration.CoreModelAdapter.ToPublic(Core.Excel.ExcelStyleConverter.ConvertOriginal(cell));
}
