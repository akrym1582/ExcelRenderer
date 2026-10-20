using ExcelRenderer.Abstractions;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Fonts.TrueType;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.PostScript;

/// <summary>Collects document-shared Type 42 subsets and 256-code encoding banks.</summary>
internal sealed class PostScriptFontCollection
{
    private readonly Dictionary<string, Font> fonts = new(StringComparer.Ordinal);
    private readonly CancellationToken token;
    private bool prepared;

    /// <summary>Initializes a new instance of the <see cref="PostScriptFontCollection"/> class.</summary>
    /// <param name="token">The conversion token.</param>
    internal PostScriptFontCollection(CancellationToken token) => this.token = token;

    /// <summary>Collects already selected physical glyphs without measuring or resolving text.</summary>
    /// <param name="run">The finalized run.</param>
    internal void Collect(TextLayoutRun run)
    {
        var selection = run.Glyphs ?? throw new InvalidOperationException("PostScript requires finalized glyphs.");
        if (selection.Glyphs.Count == 0)
        {
            return;
        }

        var face = ConversionFontResources.Current!.GetFace(run.Run.Font);
        if (selection.FontIdentity != face.Identity)
        {
            throw new InvalidOperationException("Finalized glyphs belong to a different font snapshot.");
        }

        if (!fonts.TryGetValue(face.Identity, out var font))
        {
            font = new("ERF" + fonts.Count, face);
            fonts.Add(face.Identity, font);
        }

        foreach (var entry in Keys(run))
        {
            if (!font.Codes.ContainsKey(entry))
            {
                var code = font.NextCode;
                if (code % 256 == 0)
                {
                    font.BankNames.Clear();
                }

                if (!font.BankNames.Add(entry.Name))
                {
                    code = ((code / 256) + 1) * 256;
                    font.BankNames.Clear();
                    font.BankNames.Add(entry.Name);
                }

                font.Codes.Add(entry, code);
                font.NextCode = code + 1;
            }
        }
    }

    /// <summary>Validates rights and static outlines and subsets each physical font once.</summary>
    internal void Prepare()
    {
        foreach (var font in fonts.Values)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                font.Data = TrueTypeSubsetter.Create(font.Face.Data, font.Codes.Keys.Select(key => key.Glyph), token);
            }
            catch (NotSupportedException error)
            {
                throw new PostScriptFailure("PostScriptFontUnsupported", $"Type 42 requires an embeddable, subsettable static glyf TrueType face ({font.Face.Typeface.FamilyName}). " + error.Message);
            }

            ConversionMetrics.Report("postScript.fontBytes", font.Data.Length);
        }

        ConversionMetrics.Report("postScript.fontCount", fonts.Count);
        prepared = true;
    }

    /// <summary>Writes Type 42 resources with Unicode glyph names and shared sfnts across banks.</summary>
    /// <param name="writer">The document serializer.</param>
    internal void Write(PostScriptWriter writer)
    {
        if (!prepared)
        {
            throw new InvalidOperationException("Fonts must be prepared before writing.");
        }

        foreach (var font in fonts.Values)
        {
            var data = font.Data!;
            var sfnt = new SfntReader(data);
            var head = sfnt.Get("head", 54);
            var units = SfntReader.U16(head, 18);
            writer.Line($"%%BeginResource: font {font.Name}");
            writer.Line($"/{font.Name} 12 dict dup begin /FontName /{font.Name} def /FontType 42 def /PaintType 0 def");
            writer.Line("/FontMatrix [1 0 0 1 0 0] def");
            writer.Line($"/FontBBox [{string.Join(" ", Enumerable.Range(0, 4).Select(i => PostScriptWriter.Number(SfntReader.I16(sfnt.Get("head"), 36 + (i * 2)) / (double)units)))}] def");
            writer.Line("/Encoding 256 array 0 1 255 {1 index exch /.notdef put} for def");
            writer.Line("/CharStrings 1 dict dup begin /.notdef 0 def end readonly def /sfnts [");
            foreach (var segment in Segments(data, sfnt, token))
            {
                writer.Line("<");
                writer.Hex(data, segment.Start, segment.Length);

                // Type 42 strings have a trailing padding byte, not part of the sfnt.
                writer.Line("00>");
            }

            writer.Line("] def end definefont pop");
            writer.Line("%%EndResource");
            for (var bank = 0; bank <= (font.NextCode - 1) / 256; bank++)
            {
                var name = font.Name + "B" + bank;
                writer.Line($"%%BeginResource: font {name}");
                writer.Line($"/{name} /{font.Name} findfont dup length dict begin {{1 index /FID ne {{def}} {{pop pop}} ifelse}} forall");
                writer.Line($"/FontName /{name} def /Encoding 256 array 0 1 255 {{1 index exch /.notdef put}} for");
                foreach (var entry in font.Codes.Where(entry => entry.Value / 256 == bank))
                {
                    writer.Line($"dup {entry.Value % 256} /{entry.Key.Name} put");
                }

                writer.Line("readonly def");
                var entries = font.Codes.Where(entry => entry.Value / 256 == bank).ToArray();
                writer.Line($"/CharStrings {entries.Length + 1} dict dup begin /.notdef 0 def");
                foreach (var entry in entries)
                {
                    writer.Line($"/{entry.Key.Name} {entry.Key.Glyph} def");
                }

                writer.Line("end readonly def currentdict end definefont pop");
                writer.Line("%%EndResource");
            }
        }
    }

    /// <summary>Gets bank, byte code and original GID for each glyph in a run.</summary>
    /// <param name="run">The finalized run.</param>
    /// <returns>Codes in original run order.</returns>
    internal IEnumerable<(string Font, byte Code, ushort Glyph)> Encode(TextLayoutRun run)
    {
        if (run.Glyphs!.Glyphs.Count == 0)
        {
            yield break;
        }

        var font = fonts[run.Glyphs.FontIdentity];
        foreach (var key in Keys(run))
        {
            var code = font.Codes[key];
            yield return (font.Name + "B" + (code / 256), (byte)(code % 256), key.Glyph);
        }
    }

    /// <summary>Lists every supplied font resource in the DSC trailer.</summary>
    /// <param name="writer">The document serializer.</param>
    internal void WriteResourceComments(PostScriptWriter writer)
    {
        var first = true;
        foreach (var font in fonts.Values)
        {
            writer.Line($"{(first ? "%%DocumentSuppliedResources:" : "%%+")} font {font.Name}");
            first = false;
            for (var bank = 0; bank <= (font.NextCode - 1) / 256; bank++)
            {
                writer.Line($"%%+ font {font.Name}B{bank}");
            }
        }

        if (first)
        {
            writer.Line("%%DocumentSuppliedResources: none");
        }
    }

    private static IEnumerable<(ushort Glyph, string Name)> Keys(TextLayoutRun run)
    {
        var selection = run.Glyphs!;
        var scalars = new List<int>();
        var text = run.Run.Text;
        for (var i = 0; i < text.Length; i++)
        {
            scalars.Add(char.IsSurrogatePair(text, i) ? char.ConvertToUtf32(text, i++) : text[i]);
        }

        for (var i = 0; i < selection.Glyphs.Count; i++)
        {
            // Explicit IVS selections use the base Unicode name; the full sequence lives in metadata.
            var scalar = scalars.Count > i ? scalars[i] : 0xfffd;
            var unicode = scalar <= 0xffff ? $"uni{scalar:X4}" : $"u{scalar:X5}";
            yield return (selection.Glyphs[i], unicode);
        }
    }

    private static IEnumerable<(int Start, int Length)> Segments(byte[] data, SfntReader sfnt, CancellationToken token)
    {
        var glyphOffsets = TrueTypeFontValidator.Validate(sfnt, token);
        var glyf = sfnt.Tables["glyf"];
        var start = 0;
        while (start < data.Length)
        {
            token.ThrowIfCancellationRequested();
            var end = Math.Min(start + 65534, data.Length);
            if (end > glyf.Offset && end < glyf.Offset + glyf.Length)
            {
                var relative = end - glyf.Offset;
                var boundary = Array.BinarySearch(glyphOffsets, relative);
                if (boundary < 0)
                {
                    end = glyf.Offset + glyphOffsets[(~boundary) - 1];
                }
            }

            if (end <= start)
            {
                throw new PostScriptFailure("PostScriptFontUnsupported", "A single TrueType glyph exceeds the Type 42 string limit.");
            }

            yield return (start, end - start);
            start = end;
        }
    }

    private sealed class Font(string name, SkiaFontFace face)
    {
        internal string Name { get; } = name;

        internal SkiaFontFace Face { get; } = face;

        internal Dictionary<(ushort Glyph, string Name), int> Codes { get; } = new();

        internal byte[]? Data { get; set; }

        internal int NextCode { get; set; }

        internal HashSet<string> BankNames { get; } = new(StringComparer.Ordinal);
    }
}
