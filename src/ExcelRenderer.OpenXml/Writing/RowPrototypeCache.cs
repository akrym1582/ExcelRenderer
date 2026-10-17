using System.Collections.Generic;
using System.Xml.Linq;

namespace ExcelRenderer.OpenXml.Writing;

/// <summary>Bounds retained row XML by source bytes and entry count; callers always edit deep copies.</summary>
internal sealed class RowPrototypeCache
{
    /// <summary>Maximum UTF-8 source size of a retained row.</summary>
    internal const int MaximumRowBytes = 64 * 1024;

    /// <summary>Maximum sum of retained row source sizes.</summary>
    internal const int MaximumBytes = 256 * 1024;

    /// <summary>Maximum retained prototypes, including very small rows.</summary>
    internal const int MaximumEntries = 64;
    private readonly Dictionary<int, LinkedListNode<(int Row, int Bytes, XElement Value)>> entries = new Dictionary<int, LinkedListNode<(int Row, int Bytes, XElement Value)>>();
    private readonly LinkedList<(int Row, int Bytes, XElement Value)> recency = new LinkedList<(int Row, int Bytes, XElement Value)>();

    /// <summary>Gets the sum of retained UTF-8 source sizes.</summary>
    internal int SourceBytes { get; private set; }

    /// <summary>Gets the number of avoided XML parses.</summary>
    internal long Hits { get; private set; }

    /// <summary>Gets the number of retained prototypes.</summary>
    internal int Count => entries.Count;

    /// <summary>Returns an independent editable copy and refreshes recency.</summary>
    /// <param name="row">The source row index.</param>
    /// <returns>A deep copy, or null on a miss.</returns>
    internal XElement? Clone(int row)
    {
        if (!entries.TryGetValue(row, out var entry))
        {
            return null;
        }

        Hits++;
        recency.Remove(entry);
        recency.AddLast(entry);
        return new XElement(entry.Value.Value);
    }

    /// <summary>Retains a bounded immutable prototype and returns an editable copy.</summary>
    /// <param name="row">The source row index, absent from the cache.</param>
    /// <param name="sourceBytes">The serialized UTF-8 source size.</param>
    /// <param name="value">The original parsed row.</param>
    /// <returns>An independent row, or the unretained original when oversized.</returns>
    internal XElement Store(int row, int sourceBytes, XElement value)
    {
        if (sourceBytes > MaximumRowBytes)
        {
            return value;
        }

        while (entries.Count >= MaximumEntries || SourceBytes + sourceBytes > MaximumBytes)
        {
            var oldest = recency.First!;
            entries.Remove(oldest.Value.Row);
            SourceBytes -= oldest.Value.Bytes;
            recency.RemoveFirst();
        }

        entries.Add(row, recency.AddLast((row, sourceBytes, value)));
        SourceBytes += sourceBytes;
        return new XElement(value);
    }
}
