using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Core.Model;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ExcelRenderer.Core.Excel;

/// <summary>ClosedXML が公開していない DrawingML の機能を読み取ります。</summary>
internal static class DrawingMLReader
{
    private const double EmusPerPoint = 914400d / 72d;

    /// <summary>Reads picture anchor and drawing-order metadata.</summary>
    /// <param name="input">Workbook stream.</param>
    /// <returns>Picture metadata keyed first by sheet and then by picture name.</returns>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, DrawingPictureMetadata>> ReadPictureMetadata(Stream input)
    {
        using var document = SpreadsheetDocument.Open(input, false);
        var workbook = document.WorkbookPart;
        var result = new Dictionary<string, IReadOnlyDictionary<string, DrawingPictureMetadata>>(StringComparer.OrdinalIgnoreCase);
        if (workbook?.Workbook.Sheets is null)
        {
            return result;
        }

        foreach (var sheet in workbook.Workbook.Sheets.Elements<S.Sheet>())
        {
            var pictures = new Dictionary<string, DrawingPictureMetadata>(StringComparer.Ordinal);
            if (sheet.Id?.Value is { } id && workbook.GetPartById(id) is WorksheetPart worksheet &&
                worksheet.DrawingsPart?.WorksheetDrawing is { } drawing)
            {
                var z = 0;
                foreach (var anchor in drawing.ChildElements)
                {
                    foreach (var picture in anchor.Elements<Xdr.Picture>())
                    {
                        var name = picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value;
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }

                        pictures[name] = new(
                            ReadAnchor(anchor),
                            z);
                    }

                    z++;
                }
            }

            result[sheet.Name?.Value ?? string.Empty] = pictures;
        }

        return result;
    }

    /// <summary>Reads a common DrawingML anchor without interpreting full features.</summary>
    /// <param name="anchor">The source anchor element.</param>
    /// <returns>The neutral point coordinates.</returns>
    internal static DrawingAnchor ReadAnchor(OpenXmlElement anchor)
    {
        var from = anchor.GetFirstChild<Xdr.FromMarker>();
        var to = anchor.GetFirstChild<Xdr.ToMarker>();
        var position = anchor.GetFirstChild<Xdr.Position>();
        var extent = anchor.GetFirstChild<Xdr.Extent>();
        var kind = anchor is Xdr.TwoCellAnchor ? DrawingAnchorKind.TwoCell
            : anchor is Xdr.AbsoluteAnchor ? DrawingAnchorKind.Absolute : DrawingAnchorKind.OneCell;
        return new(
            kind,
            ReadMarkerAddress(from),
            ToPoints(from?.ColumnOffset?.Text is { } fromX ? long.Parse(fromX) : 0),
            ToPoints(from?.RowOffset?.Text is { } fromY ? long.Parse(fromY) : 0),
            ReadMarkerAddress(to),
            ToPoints(to?.ColumnOffset?.Text is { } toX ? long.Parse(toX) : 0),
            ToPoints(to?.RowOffset?.Text is { } toY ? long.Parse(toY) : 0),
            ToPoints(position?.X?.Value ?? 0),
            ToPoints(position?.Y?.Value ?? 0),
            ToPoints(extent?.Cx?.Value ?? 0),
            ToPoints(extent?.Cy?.Value ?? 0),
            (anchor as Xdr.TwoCellAnchor)?.EditAs?.InnerText);
    }

    /// <summary>Converts original DrawingML units without a pixel round trip.</summary>
    /// <param name="emu">The source units.</param>
    /// <returns>The point coordinate.</returns>
    internal static double ToPoints(long emu) => emu / EmusPerPoint;

    private static CellAddress? ReadMarkerAddress(OpenXmlCompositeElement? marker) => marker switch
    {
        Xdr.FromMarker from => new(
            (int)(from.RowId?.Text is { } row ? uint.Parse(row) + 1 : 1),
            (int)(from.ColumnId?.Text is { } column ? uint.Parse(column) + 1 : 1)),
        Xdr.ToMarker to => new(
            (int)(to.RowId?.Text is { } row ? uint.Parse(row) + 1 : 1),
            (int)(to.ColumnId?.Text is { } column ? uint.Parse(column) + 1 : 1)),
        _ => null,
    };
}
