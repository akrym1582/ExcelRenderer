namespace ExcelRenderer.Core.Fonts.TrueType;

/// <summary>Validates selected glyphs and visits composite dependencies without recursion.</summary>
internal static class TrueTypeGlyphClosure
{
    /// <summary>Computes a transitive retained-GID set including .notdef.</summary>
    /// <param name="glyf">The original bounded glyph table.</param>
    /// <param name="offsets">The validated loca offsets.</param>
    /// <param name="used">The selected glyph IDs.</param>
    /// <param name="token">The cancellation token.</param>
    /// <returns>The retained glyph flags.</returns>
    internal static bool[] Collect(ReadOnlySpan<byte> glyf, int[] offsets, IEnumerable<ushort> used, CancellationToken token)
    {
        var count = offsets.Length - 1;
        var state = new byte[count];
        var roots = new SortedSet<ushort> { 0 };
        foreach (var glyph in used)
        {
            token.ThrowIfCancellationRequested();
            if (glyph >= count)
            {
                throw SfntReader.Invalid("glyf", $"selected GID {glyph} exceeds numGlyphs {count}");
            }

            roots.Add(glyph);
        }

        var stack = new Stack<(int Glyph, bool Exit)>();
        foreach (var root in roots)
        {
            stack.Push((root, false));
            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var (glyph, exit) = stack.Pop();
                if (exit)
                {
                    state[glyph] = 2;
                    continue;
                }

                if (state[glyph] == 2)
                {
                    continue;
                }

                if (state[glyph] == 1)
                {
                    throw SfntReader.Invalid("glyf", $"composite glyph cycle at GID {glyph}");
                }

                state[glyph] = 1;
                stack.Push((glyph, true));
                var data = glyf.Slice(offsets[glyph], offsets[glyph + 1] - offsets[glyph]);
                var children = ValidateGlyph(data, count, token);
                for (var i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push((children[i], false));
                }
            }
        }

        return state.Select(value => value == 2).ToArray();
    }

    private static List<ushort> ValidateGlyph(ReadOnlySpan<byte> data, int glyphCount, CancellationToken token)
    {
        var children = new List<ushort>();
        if (data.Length == 0)
        {
            return children;
        }

        SfntReader.Range(data.Length, 0, 10, "glyf header");
        var contours = SfntReader.I16(data, 0);
        if (SfntReader.I16(data, 2) > SfntReader.I16(data, 6) || SfntReader.I16(data, 4) > SfntReader.I16(data, 8))
        {
            throw SfntReader.Invalid("glyf", "inverted bounding box");
        }

        if (contours >= 0)
        {
            ValidateSimple(data, contours, token);
            return children;
        }

        if (contours != -1)
        {
            throw SfntReader.Invalid("glyf", "invalid contour count");
        }

        var position = 10;
        var instructions = false;
        ushort flags;
        do
        {
            token.ThrowIfCancellationRequested();
            SfntReader.Range(data.Length, position, 4, "glyf component");
            flags = SfntReader.U16(data, position);
            var glyph = SfntReader.U16(data, position + 2);
            if (glyph >= glyphCount)
            {
                throw SfntReader.Invalid("glyf component", "component GID out of range");
            }

            children.Add(glyph);
            position += 4;
            var scale = flags & 0x00c8;
            if (scale is not (0 or 0x0008 or 0x0040 or 0x0080))
            {
                throw SfntReader.Invalid("glyf component", "conflicting transform flags");
            }

            var length = (flags & 1) != 0 ? 4 : 2;
            length += scale switch { 0x0008 => 2, 0x0040 => 4, 0x0080 => 8, _ => 0 };
            SfntReader.Range(data.Length, position, length, "glyf component arguments/transform");
            position += length;
            instructions |= (flags & 0x0100) != 0;
        }
        while ((flags & 0x0020) != 0);

        if (instructions)
        {
            var length = SfntReader.U16(data, position);
            SfntReader.Range(data.Length, position + 2, length, "glyf composite instructions");
        }

        return children;
    }

    private static void ValidateSimple(ReadOnlySpan<byte> data, int contours, CancellationToken token)
    {
        var position = 10;
        SfntReader.Range(data.Length, position, contours * 2, "glyf endPtsOfContours");
        var previous = -1;
        for (var i = 0; i < contours; i++)
        {
            token.ThrowIfCancellationRequested();
            var end = SfntReader.U16(data, position);
            if (end <= previous)
            {
                throw SfntReader.Invalid("glyf", "nonincreasing contour endpoints");
            }

            previous = end;
            position += 2;
        }

        // Some legal zero-contour glyphs contain only their header.
        if (contours == 0 && position == data.Length)
        {
            return;
        }

        var instructions = SfntReader.U16(data, position);
        position += 2;
        SfntReader.Range(data.Length, position, instructions, "glyf simple instructions");
        position += instructions;
        var points = previous + 1;
        var decoded = 0;
        var xBytes = 0;
        var yBytes = 0;
        while (decoded < points)
        {
            token.ThrowIfCancellationRequested();
            SfntReader.Range(data.Length, position, 1, "glyf flags");
            var flags = data[position++];
            var repetitions = 1;
            if ((flags & 8) != 0)
            {
                SfntReader.Range(data.Length, position, 1, "glyf repeated flags");
                repetitions += data[position++];
            }

            if (repetitions > points - decoded)
            {
                throw SfntReader.Invalid("glyf", "flag repetitions exceed point count");
            }

            xBytes += repetitions * ((flags & 2) != 0 ? 1 : (flags & 16) != 0 ? 0 : 2);
            yBytes += repetitions * ((flags & 4) != 0 ? 1 : (flags & 32) != 0 ? 0 : 2);
            decoded += repetitions;
        }

        SfntReader.Range(data.Length, position, xBytes + yBytes, "glyf coordinates");
    }
}
