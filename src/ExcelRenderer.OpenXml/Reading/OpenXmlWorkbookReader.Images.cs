using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DocumentFormat.OpenXml.Packaging;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ExcelRenderer.OpenXml;

/// <summary>Streams image identities and opens package-owned image data.</summary>
public sealed partial class OpenXmlWorkbookReader
{
    private readonly Dictionary<ImageInfo, ImagePart> imageParts = new Dictionary<ImageInfo, ImagePart>();
    private readonly Dictionary<int, IReadOnlyList<ImageInfo>> images = new Dictionary<int, IReadOnlyList<ImageInfo>>();

    /// <summary>Reads picture identities and anchors without opening image bytes. Call before enumerating cells.</summary>
    /// <param name="sheetIndex">Original one-based worksheet position.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Picture placements in drawing order.</returns>
    public IReadOnlyList<ImageInfo> ReadImages(int sheetIndex, CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (images.TryGetValue(sheetIndex, out var cached))
            {
                return cached;
            }

            var result = new List<ImageInfo>();
            var drawing = GetPart(sheetIndex).DrawingsPart;
            if (drawing != null)
            {
                using var reader = XmlValues.CreateReader(drawing);
                var zIndex = 0;
                while (XmlValues.Read(reader))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!reader.IsStartElement || (reader.ElementType != typeof(Xdr.OneCellAnchor) &&
                        reader.ElementType != typeof(Xdr.TwoCellAnchor) && reader.ElementType != typeof(Xdr.AbsoluteAnchor)))
                    {
                        continue;
                    }

                    var anchor = XmlValues.Load(reader);
                    foreach (var picture in anchor.Elements().Where(e => e.Name.LocalName == "pic"))
                    {
                        var blip = picture.Descendants().FirstOrDefault(e => e.Name.LocalName == "blip");
                        var id = blip?.Attributes().FirstOrDefault(a => a.Name.LocalName == "embed")?.Value;
                        if (id == null)
                        {
                            continue;
                        }

                        if (drawing.GetPartById(id) is not ImagePart part)
                        {
                            throw new InvalidDataException("Picture relationship does not point to an image part.");
                        }

                        var name = picture.Descendants().FirstOrDefault(e => e.Name.LocalName == "cNvPr")?.Attribute("name")?.Value;
                        var info = new ImageInfo(sheetIndex, id, part.Uri.ToString(), part.ContentType, name, zIndex, XmlValues.Info(anchor));
                        imageParts.Add(info, part);
                        result.Add(info);
                    }

                    zIndex++;
                }
            }

            var snapshot = result.AsReadOnly();
            images.Add(sheetIndex, snapshot);
            return snapshot;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Opens image bytes owned by this reader's package. Dispose the stream before disposing the reader.</summary>
    /// <param name="image">A placement returned by this reader.</param>
    /// <returns>A caller-disposed read stream, valid only while the reader is alive.</returns>
    public Stream OpenImageStream(ImageInfo image)
    {
        EnsureAlive();
        if (image == null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        return imageParts.TryGetValue(image, out var part)
            ? part.GetStream(FileMode.Open, FileAccess.Read)
            : throw new ArgumentException("Image does not belong to this reader.", nameof(image));
    }
}
