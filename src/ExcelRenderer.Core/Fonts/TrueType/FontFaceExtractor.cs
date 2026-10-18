using ExcelRenderer.Core.Rendering;

namespace ExcelRenderer.Core.Fonts.TrueType;

/// <summary>Normalizes a selected collection face without applying PDF format restrictions.</summary>
internal static class FontFaceExtractor
{
    /// <summary>Extracts all selected face tables except invalidated signatures.</summary>
    /// <param name="data">The immutable container bytes.</param>
    /// <param name="index">The zero-based collection index.</param>
    /// <param name="token">The cancellation token.</param>
    /// <returns>Independent standalone face bytes.</returns>
    internal static byte[] Extract(byte[] data, int index = 0, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var collection = new FontCollectionReader(data, token);
        var face = collection.ReadFace(index, token);
        if (!collection.IsCollection)
        {
            return (byte[])data.Clone();
        }

        var tables = face.Tables.Where(entry => entry.Key != "DSIG")
            .ToDictionary(entry => entry.Key, entry => new ReadOnlyMemory<byte>(data, entry.Value.Offset, entry.Value.Length), StringComparer.Ordinal);
        var result = ConversionMetrics.Measure("fontFaceExtract", () => SfntWriter.Write(face.Version, tables, token));
        ConversionMetrics.Report("fontFaceExtracted", 1);
        return result;
    }
}
