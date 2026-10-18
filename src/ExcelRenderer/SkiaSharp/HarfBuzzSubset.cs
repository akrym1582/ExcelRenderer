using System.Runtime.InteropServices;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Calls the pinned, separately distributed HarfBuzz subset adapter without changing glyph IDs.</summary>
internal static class HarfBuzzSubset
{
    private const string Library = "ExcelRenderer.HarfBuzzSubset";

    /// <summary>Subsets an sfnt while retaining original glyph IDs and composite dependencies.</summary>
    /// <param name="data">The original face bytes.</param>
    /// <param name="glyphs">The used glyph IDs.</param>
    /// <param name="token">The conversion cancellation token.</param>
    /// <returns>The subset face bytes.</returns>
    internal static byte[] Create(byte[] data, IEnumerable<ushort> glyphs, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var ids = glyphs.Select(glyph => (uint)glyph).Distinct().OrderBy(glyph => glyph).ToArray();
        IntPtr blob;
        try
        {
            blob = Subset(data, checked((uint)data.Length), ids, checked((uint)ids.Length));
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new InvalidOperationException("Skia PDF requires the ExcelRenderer.HarfBuzzSubset native adapter for this OS/RID. Build and deploy it using scripts/build-harfbuzz-subset.py; full embedding is not used as a fallback.", error);
        }

        if (blob == IntPtr.Zero)
        {
            throw new InvalidDataException("HarfBuzz could not subset the selected font.");
        }

        try
        {
            token.ThrowIfCancellationRequested();
            var pointer = BlobData(blob, out var length);
            if (pointer == IntPtr.Zero || length == 0 || length > int.MaxValue)
            {
                throw new InvalidDataException("HarfBuzz returned an empty or oversized subset.");
            }

            var result = new byte[(int)length];
            Marshal.Copy(pointer, result, 0, result.Length);
            return result;
        }
        finally
        {
            BlobDestroy(blob);
        }
    }

    [DllImport(Library, EntryPoint = "er_subset", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr Subset(byte[] data, uint length, uint[] glyphs, uint count);

    [DllImport(Library, EntryPoint = "er_blob_data", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr BlobData(IntPtr blob, out uint length);

    [DllImport(Library, EntryPoint = "er_blob_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void BlobDestroy(IntPtr blob);
}
