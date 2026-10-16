using ExcelRenderer.TestFixtures;
using S = DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using SkiaSharp;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ExcelRenderer.Core.Tests;

internal static class DrawingAnchorWorkbookFixture
{
    internal static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(), $"anchors-{Guid.NewGuid():N}.xlsx");
        var rows = new S.SheetData();
        var columns = new S.Columns();
        for (var column = 1; column <= 8; column++) columns.Append(new S.Column { Min = (uint)column, Max = (uint)column, Width = 1.710625 + (column % 3) * 0.25, CustomWidth = true });
        for (var row = 1; row <= 10; row++)
        {
            rows.Append(new S.Row(Enumerable.Range(1, 8).Select(column => SdkWorkbook.Text(((char)('A' + column - 1)).ToString() + row, "x"))) { RowIndex = (uint)row, Height = 8 + row, CustomHeight = true });
        }
        using (var input = SdkWorkbook.Create(new S.Worksheet(columns, rows), SdkWorkbook.Styles(), name: "Anchors"))
        {
            using (var document = SpreadsheetDocument.Open(input, true))
            {
                var names = new[] { "absolute", "one", "two" };
                var colors = new[] { SKColors.Red, SKColors.Green, SKColors.Blue };
                for (var index = 0; index < names.Length; index++) SdkWorkbook.Image(document.WorkbookPart!.WorksheetParts.Single(), TestSupport.Png(colors[index]), names[index], 3, 2);
            }
            File.WriteAllBytes(path, input.ToArray());
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

    private static T Marker<T>(int column, int row, long x, long y) where T : OpenXmlCompositeElement, new()
    {
        var marker = new T();
        marker.Append(new Xdr.ColumnId(column.ToString()), new Xdr.ColumnOffset(x.ToString()),
            new Xdr.RowId(row.ToString()), new Xdr.RowOffset(y.ToString()));
        return marker;
    }
}
