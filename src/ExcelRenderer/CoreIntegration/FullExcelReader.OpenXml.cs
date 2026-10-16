using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Model;
using ExcelRenderer.OpenXml;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Supplies full product metadata to the streaming source path.</summary>
internal sealed partial class FullExcelReader
{
    /// <inheritdoc/>
    protected override bool DeferOpenXmlDiagnostics => true;

    /// <inheritdoc/>
    protected override CellStyle NormalizeCellStyle(CellStyle style) => style;

    /// <inheritdoc/>
    protected override ReportSheet CompleteSheet(string sheetName, ReportSheet sheet, CoreSheetReadContext context)
    {
        var data = (FullWorkbookData)context.Metadata.ExtensionData!;
        var pictures = data.Pictures.GetValueOrDefault(sheet.Name);
        return sheet with
        {
            Images = sheet.Images?.Select(image => image.SourceImage is { } source
                ? image with { ExtensionData = new FullPictureData(Picture(source, image.DrawingAnchor!)), SourceImage = null }
                : image.Name is { } name && pictures?.GetValueOrDefault(name) is { } picture ? image with { ExtensionData = new FullPictureData(picture) } : image).ToArray(),
            ExtensionData = new FullSheetData(context.IncludeBody ? data.Shapes.GetValueOrDefault(sheet.Name, []) : [], data.Hyperlinks.GetValueOrDefault(sheet.Name))
            {
                CellReadDiagnostics = context.Metadata.CellDiagnostics.GetValueOrDefault(sheet.Name, []).Select(CoreDiagnosticAdapter.ToPublic).ToArray(),
                UnsupportedFeatures = context.IncludeBody && detectUnsupportedFeatures ? data.UnsupportedFeatures.GetValueOrDefault(sheet.Name, []).Concat(context.ConditionalFormattingRanges.Select((ranges, index) => new UnsupportedFeature(
                    "UnsupportedConditionalFormatting",
                    "A conditional formatting rule exists but its visual effects are not rendered.",
                    "conditional-format-" + (index + 1),
                    ranges.Select(CoreModelAdapter.ToPublic).ToArray()))).ToArray() : [],
            },
        };
    }

    private static Excel.DrawingPictureMetadata Picture(ImageInfo source, Core.Model.DrawingAnchor anchor)
    {
        var picture = OpenXmlStyleConverter.Child(source.Anchor, "pic");
        var crop = OpenXmlStyleConverter.Child(OpenXmlStyleConverter.Child(picture, "blipFill"), "srcRect");
        var transform = OpenXmlStyleConverter.Child(OpenXmlStyleConverter.Child(picture, "spPr"), "xfrm");
        var rectangle = crop is null ? null : new Model.ImageCrop(
            OpenXmlStyleConverter.Number(crop, "l", 0) / 100000,
            OpenXmlStyleConverter.Number(crop, "t", 0) / 100000,
            OpenXmlStyleConverter.Number(crop, "r", 0) / 100000,
            OpenXmlStyleConverter.Number(crop, "b", 0) / 100000);
        return new(
            CoreModelAdapter.ToPublic(anchor),
            rectangle,
            OpenXmlStyleConverter.Number(transform, "rot", 0) / 60000,
            OpenXmlStyleConverter.Boolean(transform, "flipH"),
            OpenXmlStyleConverter.Boolean(transform, "flipV"),
            source.ZIndex);
    }
}
