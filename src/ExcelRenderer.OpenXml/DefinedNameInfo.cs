using System.Collections.Generic;

namespace ExcelRenderer.OpenXml;

/// <summary>Immutable DefinedNameInfo snapshot. XML attribute names and source units are preserved.</summary>
public sealed class DefinedNameInfo
{
    /// <summary>Initializes a new instance of the <see cref="DefinedNameInfo"/> class.</summary>
    /// <param name="name">The name value.</param>
    /// <param name="rawText">The rawText value.</param>
    /// <param name="localSheetId">The localSheetId value.</param>
    internal DefinedNameInfo(string name, string rawText, int? localSheetId)
    {
        Name = name;
        RawText = rawText;
        LocalSheetId = localSheetId;
    }

    /// <summary>Gets the Name metadata.</summary>
    public string Name { get; }

    /// <summary>Gets the RawText metadata.</summary>
    public string RawText { get; }

    /// <summary>Gets the LocalSheetId metadata.</summary>
    public int? LocalSheetId { get; }
}
