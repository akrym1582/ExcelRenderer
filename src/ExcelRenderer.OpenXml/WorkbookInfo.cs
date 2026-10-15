using System.Collections.Generic;

namespace ExcelRenderer.OpenXml;

/// <summary>Immutable WorkbookInfo snapshot. XML attribute names and source units are preserved.</summary>
public sealed class WorkbookInfo
{
    /// <summary>Initializes a new instance of the <see cref="WorkbookInfo"/> class.</summary>
    /// <param name="date1904">The date1904 value.</param>
    /// <param name="definedNames">The definedNames value.</param>
    /// <param name="defaultFont">The defaultFont value.</param>
    /// <param name="theme">The theme value.</param>
    internal WorkbookInfo(bool date1904, IReadOnlyList<DefinedNameInfo> definedNames, ElementInfo defaultFont, ElementInfo? theme)
    {
        Date1904 = date1904;
        DefinedNames = definedNames;
        DefaultFont = defaultFont;
        Theme = theme;
    }

    /// <summary>Gets a value indicating whether this flag is set.</summary>
    public bool Date1904 { get; }

    /// <summary>Gets the DefinedNames metadata.</summary>
    public IReadOnlyList<DefinedNameInfo> DefinedNames { get; }

    /// <summary>Gets the DefaultFont metadata.</summary>
    public ElementInfo DefaultFont { get; }

    /// <summary>Gets the Theme metadata.</summary>
    public ElementInfo? Theme { get; }
}
