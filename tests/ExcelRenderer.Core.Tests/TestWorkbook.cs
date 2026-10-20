using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Core.Tests;

/// <summary>Small SDK fixture factory. Inputs are SDK elements; no cell editing abstraction is provided.</summary>
internal static class TestWorkbook
{
    internal static MemoryStream Create(S.Worksheet worksheet, S.Stylesheet styles, string name = "data")
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new S.Workbook(new S.Sheets());
            var part = workbook.AddNewPart<WorksheetPart>();
            part.Worksheet = worksheet;
            workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(part), SheetId = 1, Name = name });
            workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = styles;
        }
        stream.Position = 0;
        return stream;
    }

    internal static WorksheetPart AddSheet(WorkbookPart workbook, S.Worksheet worksheet, string name, S.SheetStateValues? state = null)
    {
        var part = workbook.AddNewPart<WorksheetPart>();
        part.Worksheet = worksheet;
        workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(part), SheetId = (uint)workbook.Workbook.Sheets.ChildElements.Count + 1, Name = name, State = state });
        return part;
    }

    internal static void Image(WorksheetPart worksheet, byte[] bytes, string name = "image", int row = 0, int column = 0)
    {
        var drawing = worksheet.DrawingsPart ?? worksheet.AddNewPart<DrawingsPart>();
        drawing.WorksheetDrawing ??= new DocumentFormat.OpenXml.Drawing.Spreadsheet.WorksheetDrawing();
        if (worksheet.Worksheet.GetFirstChild<S.Drawing>() is null) worksheet.Worksheet.Append(new S.Drawing { Id = worksheet.GetIdOfPart(drawing) });
        var image = drawing.AddImagePart(ImagePartType.Png);
        using (var stream = new MemoryStream(bytes)) image.FeedData(stream);
        var picture = new DocumentFormat.OpenXml.Drawing.Spreadsheet.Picture(
            new DocumentFormat.OpenXml.Drawing.Spreadsheet.NonVisualPictureProperties(
                new DocumentFormat.OpenXml.Drawing.Spreadsheet.NonVisualDrawingProperties { Id = (uint)drawing.WorksheetDrawing.ChildElements.Count + 1, Name = name },
                new DocumentFormat.OpenXml.Drawing.Spreadsheet.NonVisualPictureDrawingProperties()),
            new DocumentFormat.OpenXml.Drawing.Spreadsheet.BlipFill(
                new DocumentFormat.OpenXml.Drawing.Blip { Embed = drawing.GetIdOfPart(image) },
                new DocumentFormat.OpenXml.Drawing.Stretch(new DocumentFormat.OpenXml.Drawing.FillRectangle())),
            new DocumentFormat.OpenXml.Drawing.Spreadsheet.ShapeProperties(
                new DocumentFormat.OpenXml.Drawing.Transform2D(new DocumentFormat.OpenXml.Drawing.Offset { X = 0, Y = 0 }, new DocumentFormat.OpenXml.Drawing.Extents { Cx = 28575, Cy = 28575 }),
                new DocumentFormat.OpenXml.Drawing.PresetGeometry(new DocumentFormat.OpenXml.Drawing.AdjustValueList()) { Preset = DocumentFormat.OpenXml.Drawing.ShapeTypeValues.Rectangle }));
        drawing.WorksheetDrawing.Append(new DocumentFormat.OpenXml.Drawing.Spreadsheet.OneCellAnchor(
            new DocumentFormat.OpenXml.Drawing.Spreadsheet.FromMarker(new DocumentFormat.OpenXml.Drawing.Spreadsheet.ColumnId(column.ToString()), new DocumentFormat.OpenXml.Drawing.Spreadsheet.ColumnOffset("0"), new DocumentFormat.OpenXml.Drawing.Spreadsheet.RowId(row.ToString()), new DocumentFormat.OpenXml.Drawing.Spreadsheet.RowOffset("0")),
            new DocumentFormat.OpenXml.Drawing.Spreadsheet.Extent { Cx = 28575, Cy = 28575 }, picture, new DocumentFormat.OpenXml.Drawing.Spreadsheet.ClientData()));
    }

    internal static S.Cell Text(string address, string text, uint style = 0) => new(new S.InlineString(new S.Text(text) { Space = SpaceProcessingModeValues.Preserve }))
    {
        CellReference = address, DataType = S.CellValues.InlineString, StyleIndex = style,
    };

    internal static S.Stylesheet Styles() => new(
        new S.Fonts(new S.Font(new S.FontSize { Val = 11 }, new S.FontName { Val = "Calibri" })),
        new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }), new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 })),
        new S.Borders(new S.Border()),
        new S.CellStyleFormats(new S.CellFormat()),
        new S.CellFormats(new S.CellFormat()),
        new S.CellStyles(new S.CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }));
}
