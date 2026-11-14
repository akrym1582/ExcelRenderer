using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.OpenXml.Writing.Plans;
using A = DocumentFormat.OpenXml.Drawing;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ExcelRenderer.OpenXml.Writing;

/// <summary>Embeds evaluated pictures after final row placement.</summary>
public static partial class OpenXmlWorkbookWriter
{
    private static void AddImages(Stream stage, WorkbookEditPlan plan, CancellationToken token)
    {
        if (!plan.Sheets.Any(sheet => sheet.Rows.Any(row => row.Values.Any(value => value.Image is not null))))
        {
            return;
        }

        stage.Position = 0;
        using var package = SpreadsheetDocument.Open(stage, true);
        var workbook = package.WorkbookPart!;
        var sheets = workbook.Workbook.Sheets!.Elements<S.Sheet>().ToArray();
        foreach (var sheet in plan.Sheets)
        {
            var part = (WorksheetPart)workbook.GetPartById(sheets[sheet.SheetIndex - 1].Id!);
            foreach (var row in sheet.Rows)
            {
                foreach (var edit in row.Values)
                {
                    token.ThrowIfCancellationRequested();
                    if (edit.Image is not { } image)
                    {
                        continue;
                    }

                    var drawings = part.DrawingsPart;
                    if (drawings is null)
                    {
                        drawings = part.AddNewPart<DrawingsPart>();
                        drawings.WorksheetDrawing = new Xdr.WorksheetDrawing();
                        part.Worksheet.AddChild(new S.Drawing { Id = part.GetIdOfPart(drawings) }, true);
                    }

                    var imagePart = drawings.AddImagePart(image.ContentType);
                    using (var bytes = new MemoryStream(image.Data, false))
                    {
                        imagePart.FeedData(bytes);
                    }

                    var drawing = drawings.WorksheetDrawing!;
                    var id = checked(drawing.Descendants<Xdr.NonVisualDrawingProperties>().Select(item => item.Id?.Value ?? 0U).DefaultIfEmpty().Max() + 1);
                    var width = Emu(image.Width);
                    var height = Emu(image.Height);
                    var picture = new Xdr.Picture(
                        new Xdr.NonVisualPictureProperties(
                            new Xdr.NonVisualDrawingProperties { Id = id, Name = "Mapped image " + id.ToString(CultureInfo.InvariantCulture) },
                            new Xdr.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true })),
                        new Xdr.BlipFill(new A.Blip { Embed = drawings.GetIdOfPart(imagePart) }, new A.Stretch(new A.FillRectangle())),
                        new Xdr.ShapeProperties(
                            new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = width, Cy = height }),
                            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
                    drawing.Append(new Xdr.OneCellAnchor(
                        new Xdr.FromMarker(
                            new Xdr.ColumnId((edit.Column - 1).ToString(CultureInfo.InvariantCulture)),
                            new Xdr.ColumnOffset(Emu(image.OffsetX).ToString(CultureInfo.InvariantCulture)),
                            new Xdr.RowId((row.OutputRow - 1).ToString(CultureInfo.InvariantCulture)),
                            new Xdr.RowOffset(Emu(image.OffsetY).ToString(CultureInfo.InvariantCulture))),
                        new Xdr.Extent { Cx = width, Cy = height },
                        picture,
                        new Xdr.ClientData()));
                }
            }
        }
    }

    private static long Emu(double points) => checked((long)Math.Round(points * 12700, MidpointRounding.AwayFromZero));
}
