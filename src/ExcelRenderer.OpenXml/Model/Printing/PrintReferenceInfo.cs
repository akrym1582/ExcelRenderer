using System.Collections.Generic;

namespace ExcelRenderer.OpenXml;

/// <summary>Immutable PrintReferenceInfo snapshot. XML attribute names and source units are preserved.</summary>
public sealed class PrintReferenceInfo
{
    /// <summary>Initializes a new instance of the <see cref="PrintReferenceInfo"/> class.</summary>
    /// <param name="name">The name value.</param>
    /// <param name="rawText">The rawText value.</param>
    /// <param name="ranges">The ranges value.</param>
    internal PrintReferenceInfo(string name, string rawText, IReadOnlyList<PrintRangeInfo> ranges)
    {
        Name = name;
        RawText = rawText;
        Ranges = ranges;
    }

    /// <summary>Gets the Name metadata.</summary>
    public string Name { get; }

    /// <summary>Gets the RawText metadata.</summary>
    public string RawText { get; }

    /// <summary>Gets the Ranges metadata.</summary>
    public IReadOnlyList<PrintRangeInfo> Ranges { get; }
}
