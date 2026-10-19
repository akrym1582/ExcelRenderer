using System.Collections.Generic;

namespace ExcelRenderer.OpenXml.Reader.Model.Cells;

/// <summary>Immutable FormulaInfo snapshot. XML attribute names and source units are preserved.</summary>
public sealed class FormulaInfo
{
    /// <summary>Initializes a new instance of the <see cref="FormulaInfo"/> class.</summary>
    /// <param name="rawFormula">The rawFormula value.</param>
    /// <param name="kind">The kind value.</param>
    /// <param name="sharedIndex">The sharedIndex value.</param>
    /// <param name="reference">The reference value.</param>
    /// <param name="hasCachedResult">The hasCachedResult value.</param>
    /// <param name="masterAddress">The masterAddress value.</param>
    /// <param name="masterFormula">The masterFormula value.</param>
    /// <param name="masterReference">The masterReference value.</param>
    /// <param name="attributes">The attributes value.</param>
    internal FormulaInfo(string rawFormula, string kind, int? sharedIndex, string? reference, bool hasCachedResult, CellAddress? masterAddress, string? masterFormula, string? masterReference, IReadOnlyDictionary<string, string> attributes)
    {
        RawFormula = rawFormula;
        Kind = kind;
        SharedIndex = sharedIndex;
        Reference = reference;
        HasCachedResult = hasCachedResult;
        MasterAddress = masterAddress;
        MasterFormula = masterFormula;
        MasterReference = masterReference;
        Attributes = attributes;
    }

    /// <summary>Gets the original formula text without a leading equals sign; shared followers may be empty.</summary>
    public string RawFormula { get; }

    /// <summary>Gets the Kind metadata.</summary>
    public string Kind { get; }

    /// <summary>Gets the SharedIndex metadata.</summary>
    public int? SharedIndex { get; }

    /// <summary>Gets the Reference metadata.</summary>
    public string? Reference { get; }

    /// <summary>Gets a value indicating whether this flag is set.</summary>
    public bool HasCachedResult { get; }

    /// <summary>Gets the shared or array master coordinate, including masters appearing later in XML.</summary>
    public CellAddress? MasterAddress { get; }

    /// <summary>Gets the MasterFormula metadata.</summary>
    public string? MasterFormula { get; }

    /// <summary>Gets the MasterReference metadata.</summary>
    public string? MasterReference { get; }

    /// <summary>Gets the all original formula attributes, including dynamic-array or data-table metadata.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; }
}
