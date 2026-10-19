namespace ExcelRenderer.Core.Fonts.TrueType;

/// <summary>Reads TTC v1/v2 metadata without copying any face tables.</summary>
internal sealed class FontCollectionReader
{
    /// <summary>Initializes a new instance of the <see cref="FontCollectionReader"/> class.</summary>
    /// <param name="data">The container bytes.</param>
    /// <param name="token">The cancellation token.</param>
    internal FontCollectionReader(byte[] data, CancellationToken token = default)
    {
        Data = data;
        SfntReader.Range(data.Length, 0, 12, "sfnt header");
        IsCollection = SfntReader.U32(data, 0) == 0x74746366;
        if (!IsCollection)
        {
            FaceOffsets = new[] { 0 };
            return;
        }

        var version = SfntReader.U32(data, 4);
        if (version is not (0x00010000 or 0x00020000))
        {
            throw SfntReader.Invalid("ttcf", "unsupported TTC version");
        }

        var count = SfntReader.Offset(data, 8, "ttcf");
        var extra = version == 0x00020000 ? 12 : 0;
        if (count == 0 || count > (data.Length - 12 - extra) / 4)
        {
            throw SfntReader.Invalid("ttcf", "invalid numFonts or face offset array");
        }

        var headerLength = 12 + (count * 4) + extra;
        Reserved.Add((0, headerLength));
        FaceOffsets = new int[count];
        var directories = new HashSet<int>();
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var offset = SfntReader.Offset(data, 12 + (i * 4), "ttcf");
            SfntReader.Range(data.Length, offset, 12, "ttcf face directory");
            var length = 12 + (SfntReader.U16(data, offset + 4) * 16);
            SfntReader.Range(data.Length, offset, length, "ttcf face directory");
            if (offset < headerLength || (offset & 3) != 0)
            {
                throw SfntReader.Invalid("ttcf", "invalid face directory offset");
            }

            FaceOffsets[i] = offset;
            if (directories.Add(offset))
            {
                Reserved.Add((offset, length));
            }
        }

        if (extra != 0)
        {
            var position = headerLength - 12;
            var tag = SfntReader.U32(data, position);
            var length = SfntReader.Offset(data, position + 4, "ttcf DSIG");
            var offset = SfntReader.Offset(data, position + 8, "ttcf DSIG");
            if (tag == 0 ? length != 0 || offset != 0 : tag != 0x44534947 || length == 0)
            {
                throw SfntReader.Invalid("ttcf DSIG", "invalid signature descriptor");
            }

            SfntReader.Range(data.Length, offset, length, "ttcf DSIG");
            if (length != 0)
            {
                Reserved.Add((offset, length));
            }
        }

        var ordered = Reserved.OrderBy(range => range.Start).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            if ((long)ordered[i - 1].Start + ordered[i - 1].Length > ordered[i].Start)
            {
                throw SfntReader.Invalid("ttcf", "overlapping headers, directories or DSIG");
            }
        }
    }

    /// <summary>Gets a value indicating whether this is a collection.</summary>
    internal bool IsCollection { get; }

    /// <summary>Gets the face directory offsets.</summary>
    internal int[] FaceOffsets { get; }

    private byte[] Data { get; }

    private List<(int Start, int Length)> Reserved { get; } = new();

    /// <summary>Reads only the requested face, allowing shared tables between faces.</summary>
    /// <param name="index">The zero-based face index.</param>
    /// <param name="token">The cancellation token.</param>
    /// <returns>The selected face directory.</returns>
    internal SfntReader ReadFace(int index, CancellationToken token = default)
    {
        if (index < 0 || index >= FaceOffsets.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Font collection index must be in [0, {FaceOffsets.Length - 1}].");
        }

        return new(Data, FaceOffsets[index], IsCollection ? Reserved : null, token);
    }
}
