using ExcelRenderer.OpenXml.Model.Workbook;

namespace ExcelRenderer.OpenXml.Model.Drawing;

/// <summary>A picture placement with package identity and source DrawingML anchor units.</summary>
public sealed class ImageInfo
{
    /// <summary>Initializes a new instance of the <see cref="ImageInfo"/> class.</summary>
    /// <param name="sheetIndex">Original sheet position.</param>
    /// <param name="relationshipId">Drawing-local image relationship.</param>
    /// <param name="partUri">Package-unique image part identity.</param>
    /// <param name="contentType">Image media type.</param>
    /// <param name="name">Original picture name.</param>
    /// <param name="zIndex">Drawing order.</param>
    /// <param name="anchor">Source DrawingML anchor, including picture transforms.</param>
    internal ImageInfo(int sheetIndex, string relationshipId, string partUri, string contentType, string? name, int zIndex, ElementInfo anchor)
    {
        SheetIndex = sheetIndex;
        RelationshipId = relationshipId;
        PartUri = partUri;
        ContentType = contentType;
        Name = name;
        ZIndex = zIndex;
        Anchor = anchor;
    }

    /// <summary>Gets the original one-based sheet position.</summary>
    public int SheetIndex { get; }

    /// <summary>Gets the drawing-local image relationship.</summary>
    public string RelationshipId { get; }

    /// <summary>Gets the package-unique part identity, shared by repeated placements.</summary>
    public string PartUri { get; }

    /// <summary>Gets the image media type.</summary>
    public string ContentType { get; }

    /// <summary>Gets the source picture name.</summary>
    public string? Name { get; }

    /// <summary>Gets the drawing order.</summary>
    public int ZIndex { get; }

    /// <summary>Gets the source anchor metadata in DrawingML units.</summary>
    public ElementInfo Anchor { get; }
}
