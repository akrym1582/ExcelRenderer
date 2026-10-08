using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using SkiaSharp;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ExcelRenderer.Tests;

/// <summary>絶対・単一セル・二セルの画像アンカーを持つ保存済み XLSX を作成します。</summary>
internal static class DrawingAnchorWorkbookFixture
{
    /// <summary>不均等な行高・列幅と赤緑青の画像を持つブックを保存し、三種類の DrawingML アンカーへ書き換えます。</summary>
    internal static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(), $"anchors-{Guid.NewGuid():N}.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Anchors");
            for (var column = 1; column <= 8; column++) { sheet.Column(column).Width = 1 + (column % 3) * 0.25; }
            for (var row = 1; row <= 10; row++)
            {
                sheet.Row(row).Height = 8 + row;
                for (var column = 1; column <= 8; column++) { sheet.Cell(row, column).Value = "x"; }
            }
            var names = new[] { "absolute", "one", "two" };
            var colors = new[] { SKColors.Red, SKColors.Green, SKColors.Blue };
            for (var i = 0; i < names.Length; i++)
            {
                using var bitmap = new SKBitmap(2, 2); bitmap.Erase(colors[i]);
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = new MemoryStream(data.ToArray());
                sheet.AddPicture(stream, names[i]).MoveTo(sheet.Cell(4, 3));
            }
            workbook.SaveAs(path);
        }
        using (var document = SpreadsheetDocument.Open(path, true))
        {
            var drawing = document.WorkbookPart!.WorksheetParts.Single().DrawingsPart!.WorksheetDrawing!;
            var pictures = drawing.Descendants<Xdr.Picture>().ToDictionary(
                p => p.NonVisualPictureProperties!.NonVisualDrawingProperties!.Name!.Value!,
                p => (Xdr.Picture)p.CloneNode(true));
            drawing.RemoveAllChildren();
            drawing.Append(
                new Xdr.AbsoluteAnchor(new Xdr.Position { X = 190500, Y = 381000 },
                    new Xdr.Extent { Cx = 571500, Cy = 762000 }, pictures["absolute"], new Xdr.ClientData()),
                new Xdr.OneCellAnchor(Marker<Xdr.FromMarker>(2, 3, 95250, 190500),
                    new Xdr.Extent { Cx = 381000, Cy = 285750 }, pictures["one"], new Xdr.ClientData()),
                new Xdr.TwoCellAnchor(Marker<Xdr.FromMarker>(2, 3, 95250, 190500),
                    Marker<Xdr.ToMarker>(4, 5, 47625, 95250), pictures["two"], new Xdr.ClientData())
                { EditAs = Xdr.EditAsValues.OneCell });
            drawing.Save();
        }
        return path;
    }

    /// <summary>指定した行列番号から DrawingML のアンカー始点または終点を構築します。</summary>
    /// <param name="column">アンカーの列番号（0 始まり）。</param>
    /// <param name="row">アンカーの行番号（0 始まり）。</param>
    /// <param name="x">セル左端からの水平オフセット（EMU）。</param>
    /// <param name="y">セル上端からの垂直オフセット（EMU）。</param>
    private static T Marker<T>(int column, int row, long x, long y) where T : OpenXmlCompositeElement, new()
    {
        var marker = new T();
        marker.Append(new Xdr.ColumnId(column.ToString()), new Xdr.ColumnOffset(x.ToString()),
            new Xdr.RowId(row.ToString()), new Xdr.RowOffset(y.ToString()));
        return marker;
    }
}
