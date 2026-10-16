using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.OpenXml;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>Creates explicit SDK elements for deterministic benchmark inputs.</summary>
internal static class FixtureWorkbook
{
    /// <summary>Creates a text cell without a shared-string side effect.</summary>
    /// <param name="row">One-based row.</param>
    /// <param name="column">One-based column.</param>
    /// <param name="text">Stored text.</param>
    /// <param name="style">Cell format index.</param>
    /// <returns>The SDK cell.</returns>
    internal static S.Cell Text(int row, int column, string text, uint style = 0) => new(new S.InlineString(new S.Text(text) { Space = SpaceProcessingModeValues.Preserve }))
    {
        CellReference = new CellAddress(row, column).ToString(),
        DataType = S.CellValues.InlineString,
        StyleIndex = style,
    };

    /// <summary>Adds a worksheet to a benchmark package.</summary>
    /// <param name="workbook">Workbook owning the sheet.</param>
    /// <param name="name">Worksheet name.</param>
    /// <param name="worksheet">Complete SDK worksheet.</param>
    /// <returns>The added part.</returns>
    internal static WorksheetPart Sheet(WorkbookPart workbook, string name, S.Worksheet worksheet)
    {
        var part = workbook.AddNewPart<WorksheetPart>();
        part.Worksheet = worksheet;
        workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(part), SheetId = (uint)workbook.Workbook.Sheets.ChildElements.Count + 1, Name = name });
        return part;
    }

    /// <summary>Creates a minimal style table with an optional painted cell format.</summary>
    /// <param name="bold">Whether the regular fixture font is bold.</param>
    /// <param name="wrap">Whether painted cells wrap.</param>
    /// <returns>The SDK style table.</returns>
    internal static S.Stylesheet Styles(bool bold = false, bool wrap = false) => new(
        new S.Fonts(new S.Font(new S.Bold { Val = bold }, new S.FontSize { Val = 10 }, new S.FontName { Val = "Noto Sans JP" })),
        new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }), new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 }), new S.Fill(new S.PatternFill(new S.ForegroundColor { Rgb = "FFD3D3D3" }, new S.BackgroundColor { Indexed = 64 }) { PatternType = S.PatternValues.Solid })),
        new S.Borders(new S.Border()),
        new S.CellStyleFormats(new S.CellFormat()),
        new S.CellFormats(new S.CellFormat(), new S.CellFormat(new S.Alignment { WrapText = wrap }) { FillId = 2, ApplyFill = true, ApplyAlignment = true }),
        new S.CellStyles(new S.CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }));

    /// <summary>Adds an image with its original bytes and explicit anchor dimensions.</summary>
    /// <param name="worksheet">Image owner.</param>
    /// <param name="bytes">Encoded PNG.</param>
    /// <param name="row">Zero-based anchor row.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    internal static void Image(WorksheetPart worksheet, byte[] bytes, int row, int width, int height)
    {
        var drawing = worksheet.DrawingsPart ?? worksheet.AddNewPart<DrawingsPart>();
        drawing.WorksheetDrawing ??= new DocumentFormat.OpenXml.Drawing.Spreadsheet.WorksheetDrawing();
        if (worksheet.Worksheet.GetFirstChild<S.Drawing>() == null)
        {
            worksheet.Worksheet.Append(new S.Drawing { Id = worksheet.GetIdOfPart(drawing) });
        }

        var image = drawing.AddImagePart(ImagePartType.Png);
        using (var input = new MemoryStream(bytes))
        {
            image.FeedData(input);
        }

        drawing.WorksheetDrawing.Append(new DocumentFormat.OpenXml.Drawing.Spreadsheet.OneCellAnchor(
            new DocumentFormat.OpenXml.Drawing.Spreadsheet.FromMarker(new DocumentFormat.OpenXml.Drawing.Spreadsheet.ColumnId("0"), new DocumentFormat.OpenXml.Drawing.Spreadsheet.ColumnOffset("0"), new DocumentFormat.OpenXml.Drawing.Spreadsheet.RowId(row.ToString(System.Globalization.CultureInfo.InvariantCulture)), new DocumentFormat.OpenXml.Drawing.Spreadsheet.RowOffset("0")),
            new DocumentFormat.OpenXml.Drawing.Spreadsheet.Extent { Cx = width * 9525L, Cy = height * 9525L },
            new DocumentFormat.OpenXml.Drawing.Spreadsheet.Picture(
                new DocumentFormat.OpenXml.Drawing.Spreadsheet.NonVisualPictureProperties(new DocumentFormat.OpenXml.Drawing.Spreadsheet.NonVisualDrawingProperties { Id = (uint)drawing.WorksheetDrawing.ChildElements.Count + 1, Name = "fixture" }, new DocumentFormat.OpenXml.Drawing.Spreadsheet.NonVisualPictureDrawingProperties()),
                new DocumentFormat.OpenXml.Drawing.Spreadsheet.BlipFill(new DocumentFormat.OpenXml.Drawing.Blip { Embed = drawing.GetIdOfPart(image) }, new DocumentFormat.OpenXml.Drawing.Stretch(new DocumentFormat.OpenXml.Drawing.FillRectangle())),
                new DocumentFormat.OpenXml.Drawing.Spreadsheet.ShapeProperties(new DocumentFormat.OpenXml.Drawing.Transform2D(new DocumentFormat.OpenXml.Drawing.Offset { X = 0, Y = 0 }, new DocumentFormat.OpenXml.Drawing.Extents { Cx = width * 9525L, Cy = height * 9525L }), new DocumentFormat.OpenXml.Drawing.PresetGeometry(new DocumentFormat.OpenXml.Drawing.AdjustValueList()) { Preset = DocumentFormat.OpenXml.Drawing.ShapeTypeValues.Rectangle })),
            new DocumentFormat.OpenXml.Drawing.Spreadsheet.ClientData()));
    }
}
