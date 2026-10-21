using System;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;

namespace ExcelRenderer.OpenXml.Reader.Model.ConditionalFormatting;

/// <summary>Immutable provenance for an interpreted extension; original source remains available.</summary>
public sealed class ConditionalFormattingExtensionInfo
{
    /// <summary>Initializes a new instance of the <see cref="ConditionalFormattingExtensionInfo"/> class.</summary>
    /// <param name="source">Original extension rule.</param>
    /// <param name="id">Saved link identifier.</param>
    internal ConditionalFormattingExtensionInfo(ElementInfo source, Guid? id)
    {
        Source = source;
        Id = id;
    }

    /// <summary>Gets the original extension rule.</summary>
    public ElementInfo Source { get; }

    /// <summary>Gets the normalized link identifier when valid.</summary>
    public Guid? Id { get; }
}
