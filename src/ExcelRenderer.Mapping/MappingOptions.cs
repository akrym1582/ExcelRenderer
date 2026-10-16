using System;
using System.Globalization;
using ExcelRenderer.OpenXml.Input;

namespace ExcelRenderer.Mapping;

/// <summary>Controls formatting and the maximum expanded worksheet size.</summary>
public sealed class MappingOptions
{
    /// <summary>Gets or sets the editing engine. Existing callers use ClosedXML.</summary>
    public MappingBackend Backend { get; set; } = MappingBackend.ClosedXml;

    /// <summary>Gets or sets an optional preflight engine-selection callback.</summary>
    public Action<MappingBackendSelection>? BackendSelected { get; set; }

    /// <summary>Gets or sets input limits and shared stage buffering settings.</summary>
    public WorkbookInputOptions Input { get; set; } = new WorkbookInputOptions();

    /// <summary>Gets or sets the default formatting culture.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    /// <summary>Gets or sets the maximum number of output rows per worksheet.</summary>
    public int MaxOutputRows { get; set; } = 100_000;
}
