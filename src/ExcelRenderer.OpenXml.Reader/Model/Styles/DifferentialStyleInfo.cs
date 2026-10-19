using ExcelRenderer.OpenXml.Reader.Model.Workbook;

namespace ExcelRenderer.OpenXml.Reader.Model.Styles;

/// <summary>A saved differential style. Missing children are unspecified; empty children explicitly clear a property.</summary>
public sealed class DifferentialStyleInfo
{
    /// <summary>Initializes a new instance of the <see cref="DifferentialStyleInfo"/> class.</summary>
    /// <param name="source">Resolved source metadata, preserving all differential children.</param>
    internal DifferentialStyleInfo(ElementInfo source)
    {
        Source = source;
    }

    /// <summary>Gets the immutable dxf, with the same color resolution as ordinary cell styles.</summary>
    public ElementInfo Source { get; }
}
