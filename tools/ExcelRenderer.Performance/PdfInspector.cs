using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace ExcelRenderer.Performance;

/// <summary>Inspects document sharing through an independent validation-only reader.</summary>
internal static class PdfInspector
{
    /// <summary>Counts reachable font/image objects and distinct embedded font streams.</summary>
    /// <param name="input">The complete output positioned at zero.</param>
    /// <returns>The object and encoded byte counts.</returns>
    internal static (int FontObjects, int ImageObjects, int FontStreams, long FontStreamBytes) Inspect(Stream input)
    {
        using var pdf = PdfDocument.Open(input);
        var visited = new HashSet<IndirectReference>();
        var fontStreams = new HashSet<StreamToken>();
        var pending = new Stack<IToken>();
        pending.Push(pdf.Structure.Catalog.CatalogDictionary);
        var fonts = 0;
        var images = 0;
        while (pending.Count > 0)
        {
            var value = pending.Pop();
            if (value is IndirectReferenceToken reference)
            {
                if (!visited.Add(reference.Data))
                {
                    continue;
                }

                value = pdf.Structure.GetObject(reference.Data).Data;
            }

            var dictionary = value is StreamToken stream ? stream.StreamDictionary : value as DictionaryToken;
            if (dictionary is not null)
            {
                fonts += dictionary.Data.TryGetValue("Type", out var type) && type is NameToken { Data: "Font" } ? 1 : 0;
                images += dictionary.Data.TryGetValue("Subtype", out var subtype) && subtype is NameToken { Data: "Image" } ? 1 : 0;
                foreach (var entry in dictionary.Data)
                {
                    if (entry.Key is "FontFile" or "FontFile2" or "FontFile3")
                    {
                        var font = entry.Value is IndirectReferenceToken fontReference ? pdf.Structure.GetObject(fontReference.Data).Data : entry.Value;
                        fontStreams.Add(font as StreamToken ?? throw new InvalidDataException("Embedded font is not a stream."));
                    }

                    pending.Push(entry.Value);
                }
            }
            else if (value is ArrayToken array)
            {
                foreach (var child in array.Data)
                {
                    pending.Push(child);
                }
            }
        }

        return (fonts, images, fontStreams.Count, fontStreams.Sum(stream => (long)stream.Data.Length));
    }
}
