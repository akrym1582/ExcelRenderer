using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ExcelRenderer.Mapping.Diagnostics;
using ExcelRenderer.TestFixtures;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ExcelRenderer.Mapping.Tests;

public sealed class TemplateDecorationTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");

    [Fact]
    public void Comments_clear_text_preserve_rows_styles_and_ignore_invalid_expressions()
    {
        using var input = Template("**// **@start-array invalid | nonsense()", "\\**//literal", "**//");
        using var output = Map(input, new { });
        using var document = SpreadsheetDocument.Open(output, false);
        var rows = document.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.Row>().ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal("", rows[0].GetFirstChild<S.Cell>()!.InnerText);
        Assert.Equal("**//literal", rows[1].GetFirstChild<S.Cell>()!.InnerText);
        Assert.Equal(0U, rows[0].GetFirstChild<S.Cell>()!.StyleIndex!.Value);
    }

    [Fact]
    public void Color_changes_only_target_font_and_preserves_numeric_type()
    {
        using var input = Template("**$.Amount | font-color($.Color)", "**$.Amount", "**$.Amount | format(\"N0\", \"en-US\") | font-color($.Color)", "**$.Amount | font-color($.Nothing)");
        using var output = Map(input, new { Amount = 12000, Color = "#ff0000", Nothing = (string?)null });
        using var document = SpreadsheetDocument.Open(output, false);
        var cells = document.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.Cell>().ToArray();
        Assert.Equal("12000", cells[0].CellValue!.Text);
        Assert.Null(cells[0].DataType);
        Assert.Equal(0U, cells[1].StyleIndex!.Value);
        Assert.Equal(0U, cells[3].StyleIndex!.Value);
        var styles = document.WorkbookPart.WorkbookStylesPart!.Stylesheet;
        var format = styles.CellFormats!.Elements<S.CellFormat>().ElementAt((int)cells[0].StyleIndex!.Value);
        Assert.Equal("FFFF0000", styles.Fonts!.Elements<S.Font>().ElementAt((int)format.FontId!.Value).Color!.Rgb!.Value);
        Assert.Equal("12,000", cells[2].InnerText);
        Assert.Empty(new OpenXmlValidator().Validate(document).Select(error => error.Description));
    }

    [Fact]
    public void Color_preserves_font_properties_inherited_from_parent_style()
    {
        var styles = SdkWorkbook.Styles(new S.CellFormat { FormatId = 1 });
        styles.Fonts!.Append(new S.Font(new S.Bold(), new S.FontSize { Val = 19 }, new S.FontName { Val = "Cambria" }));
        styles.CellStyleFormats!.Append(new S.CellFormat { FontId = 1 });
        styles.CellStyles!.Append(new S.CellStyle { Name = "Custom", FormatId = 1 });
        using var input = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(
            new S.Row(SdkWorkbook.Text("A1", "**$.Amount | font-color($.Color)")) { RowIndex = 1 })), styles);
        using var output = Map(input, new { Amount = 42, Color = "#123456" });
        using var document = SpreadsheetDocument.Open(output, false);
        var workbook = document.WorkbookPart!;
        var cell = workbook.WorksheetParts.Single().Worksheet.Descendants<S.Cell>().Single();
        var mappedStyles = workbook.WorkbookStylesPart!.Stylesheet;
        var format = mappedStyles.CellFormats!.Elements<S.CellFormat>().ElementAt((int)cell.StyleIndex!.Value);
        var font = mappedStyles.Fonts!.Elements<S.Font>().ElementAt((int)format.FontId!.Value);
        Assert.NotNull(font.Bold);
        Assert.Equal(19d, font.FontSize!.Val!.Value);
        Assert.Equal("Cambria", font.FontName!.Val!.Value);
        Assert.Equal("FF123456", font.Color!.Rgb!.Value);
    }

    [Theory]
    [InlineData("", 9525, 9525)]
    [InlineData("scale=2", 19050, 19050)]
    [InlineData("width=120", 1143000, 1143000)]
    [InlineData("height=60px", 571500, 571500)]
    [InlineData("width=90pt height=60", 1143000, 571500)]
    [InlineData("width=120 height=60 fit=contain", 571500, 571500)]
    public void Image_dimensions_offsets_bytes_and_schema(string options, long width, long height)
    {
        using var input = Template("**@image $.Logo " + options + " offset-x=8 offset-y=-4pt");
        using var output = Map(input, new { Logo = Png });
        using var document = SpreadsheetDocument.Open(output, false);
        var part = document.WorkbookPart!.WorksheetParts.Single();
        var drawing = part.DrawingsPart!;
        var anchor = drawing.WorksheetDrawing!.GetFirstChild<Xdr.OneCellAnchor>()!;
        Assert.Equal(width, anchor.Extent!.Cx!.Value);
        Assert.Equal(height, anchor.Extent.Cy!.Value);
        Assert.Equal("76200", anchor.FromMarker!.ColumnOffset!.Text);
        Assert.Equal("-50800", anchor.FromMarker.RowOffset!.Text);
        Assert.Equal("", part.Worksheet.Descendants<S.Cell>().Single().InnerText);
        using var bytes = new MemoryStream();
        drawing.ImageParts.Single().GetStream().CopyTo(bytes);
        Assert.Equal(Png, bytes.ToArray());
        Assert.Empty(new OpenXmlValidator().Validate(document).Select(error => error.Description));
    }

    [Fact]
    public void Json_base64_variable_dimensions_and_null_image()
    {
        using var input = Template("**@image $.Logo width=$.Width offset-x=$.Offset", "**@image $.Nothing");
        using var output = new MemoryStream();
        ExcelTemplateMapper.MapJson(input, output, System.Text.Json.JsonSerializer.Serialize(new { Logo = Convert.ToBase64String(Png), Width = "120px", Offset = 8, Nothing = (string?)null }));
        output.Position = 0;
        using var document = SpreadsheetDocument.Open(output, false);
        var anchors = document.WorkbookPart!.WorksheetParts.Single().DrawingsPart!.WorksheetDrawing!.Elements<Xdr.OneCellAnchor>().ToArray();
        Assert.Single(anchors);
        Assert.Equal(1143000, anchors[0].Extent!.Cx!.Value);
    }

    [Fact]
    public void Images_and_colors_follow_expanded_alias_rows()
    {
        using var input = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(
            new S.Row(SdkWorkbook.Text("A1", "**@start-array $.Items[*] as item")) { RowIndex = 1 },
            new S.Row(SdkWorkbook.Text("A2", "**@image @item.Photo width=20"), SdkWorkbook.Text("B2", "**@item.Value | font-color(@item.Color)")) { RowIndex = 2 },
            new S.Row(SdkWorkbook.Text("A3", "**@end-array")) { RowIndex = 3 })), SdkWorkbook.Styles());
        using var output = Map(input, new { Items = new[] { new { Photo = Png, Value = 1, Color = "#FF0000" }, new { Photo = Png, Value = 2, Color = "#0000FF" } } });
        using var document = SpreadsheetDocument.Open(output, false);
        var part = document.WorkbookPart!.WorksheetParts.Single();
        Assert.Equal(new[] { "0", "1" }, part.DrawingsPart!.WorksheetDrawing!.Elements<Xdr.OneCellAnchor>().Select(a => a.FromMarker!.RowId!.Text));
        Assert.Equal(new[] { "1", "2" }, part.Worksheet.Descendants<S.Cell>().Where(c => c.CellReference!.Value!.StartsWith("B")).Select(c => c.CellValue!.Text));
        Assert.Empty(new OpenXmlValidator().Validate(document).Select(error => error.Description));
    }

    [Fact]
    public void Image_ids_continue_after_existing_drawing_ids()
    {
        using var input = Template("Title", "**@image $.Items[*]");
        using (var template = SpreadsheetDocument.Open(input, true))
        {
            var part = template.WorkbookPart!.WorksheetParts.Single();
            SdkWorkbook.Image(part, Png);
            part.DrawingsPart!.WorksheetDrawing!.Descendants<Xdr.NonVisualDrawingProperties>().Single().Id = 23U;
        }

        input.Position = 0;
        using var output = Map(input, new { Items = new[] { Png, Png } });
        using var document = SpreadsheetDocument.Open(output, false);
        var drawing = document.WorkbookPart!.WorksheetParts.Single().DrawingsPart!.WorksheetDrawing!;
        Assert.Equal(new uint[] { 23, 24, 25 }, drawing.Descendants<Xdr.NonVisualDrawingProperties>().Select(item => item.Id!.Value));
    }

    [Theory]
    [InlineData("FFD8FFC0000B080001000101011100FFD9")]
    [InlineData("FFD8FFC0000B080001000101011100FFDA0008010100003F00FFD9")]
    [InlineData("FFD8FFC0000B080001000101011100FFDA0008010100003F0001FFE10008FFD9")]
    public void Incomplete_jpeg_reports_original_cell_and_preserves_output(string jpeg)
    {
        using var input = Template("Title", "**@image $.Logo");
        using var output = new MemoryStream();
        output.WriteByte(42);
        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(input, output, new { Logo = Convert.FromHexString(jpeg) }));
        Assert.Equal("A2", error.CellAddress);
        Assert.Contains("JPEG", error.Message);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData("**@image $.Logo width=-1")]
    [InlineData("**@image $.Logo width=20 scale=2")]
    [InlineData("**@image $.Logo width=20 fit=contain")]
    [InlineData("**@image $.Logo rotation=2")]
    [InlineData("**@image $.Logo width=20 width=30")]
    [InlineData("**@image $.Missing")]
    [InlineData("**@image $.Bad")]
    [InlineData("**$.Amount | font-color($.Bad)")]
    [InlineData("**$.Amount | font-color(@unknown.Color)")]
    public void Invalid_directives_report_original_cell_and_leave_output_untouched(string expression)
    {
        using var input = Template(expression);
        using var output = new MemoryStream();
        output.WriteByte(42);
        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(input, output, new { Logo = Png, Bad = "invalid", Amount = 1 }));
        Assert.Contains("A1", error.Message);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void Empty_blocks_validate_syntax_without_resolving_image_or_color_data()
    {
        using var input = Template("**@start-array $.Items[*] as item", "**@image @item.Photo width=@item.Width", "**@item.Amount | font-color(@item.Color)", "**@end-array");
        using var output = Map(input, new { Items = Array.Empty<object>() });
        using var document = SpreadsheetDocument.Open(output, false);
        Assert.Empty(document.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.Row>());
    }

    [Fact]
    public void Implicit_image_rows_and_merged_cells_preserve_placement()
    {
        using var input = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(
            new S.Row(SdkWorkbook.Text("A1", "**@image $.Items[*].Photo width=$.Items[*].Width")) { RowIndex = 1 }),
            new S.MergeCells(new S.MergeCell { Reference = "A1:B1" })), SdkWorkbook.Styles());
        using var output = Map(input, new { Items = new[] { new { Photo = Png, Width = 20 }, new { Photo = Png, Width = 40 } } });
        using var document = SpreadsheetDocument.Open(output, false);
        var part = document.WorkbookPart!.WorksheetParts.Single();
        Assert.Equal(new long[] { 190500, 381000 }, part.DrawingsPart!.WorksheetDrawing!.Elements<Xdr.OneCellAnchor>().Select(anchor => anchor.Extent!.Cx!.Value));
        Assert.Equal(new[] { "A1:B1", "A2:B2" }, part.Worksheet.Descendants<S.MergeCell>().Select(merge => merge.Reference!.Value));
    }

    [Fact]
    public void Color_parser_preserves_quoted_pipes_and_supports_reverse_modifier_order()
    {
        using var input = Template("**$['value|font-color(x)'] | font-color($['color|name']) | format(\"N0\", \"en-US\")");
        using var output = Map(input, new Dictionary<string, object> { ["value|font-color(x)"] = 1234, ["color|name"] = "#123456" });
        using var document = SpreadsheetDocument.Open(output, false);
        Assert.Equal("1,234", document.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.Cell>().Single().InnerText);
    }

    private static MemoryStream Template(params string[] expressions) => SdkWorkbook.Create(new S.Worksheet(new S.SheetData(expressions.Select((text, index) => new S.Row(SdkWorkbook.Text("A" + (index + 1), text)) { RowIndex = (uint)index + 1 }))), SdkWorkbook.Styles());

    private static MemoryStream Map(Stream input, object data)
    {
        var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, data);
        output.Position = 0;
        return output;
    }
}
