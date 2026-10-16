using System;
using System.Collections.Generic;
using System.Globalization;

namespace ExcelRenderer.OpenXml;

/// <summary>Limits and selection for a prevalidated, seekable XLSX input.</summary>
public sealed class OpenXmlReaderOptions
{
    /// <summary>Gets or sets the selected names: null selects all worksheets; an empty list selects no bodies. Matching is ordinal.</summary>
    public IReadOnlyList<string>? SelectedSheetNames { get; set; }

    /// <summary>Gets or sets the display culture. Open captures a read-only clone, defaulting to CurrentCulture.</summary>
    public CultureInfo? Culture { get; set; }

    /// <summary>Gets or sets the maximum XML characters per part; the default is 256 Mi characters.</summary>
    public long MaxCharactersInPart { get; set; } = 256L * 1024 * 1024;

    /// <summary>Gets or sets the maximum shared-string entries; the default is 1,000,000.</summary>
    public int MaxSharedStringItems { get; set; } = 1000000;

    /// <summary>Gets or sets the total UTF-16 character budget for shared strings, including phonetics; default 32 Mi.</summary>
    public long MaxSharedStringCharacters { get; set; } = 32L * 1024 * 1024;

    /// <summary>Gets or sets the record limit for each style table; the default is 100,000.</summary>
    public int MaxStyleRecords { get; set; } = 100000;

    /// <summary>Gets or sets the UTF-16 text budget per cell or shared-string entry; default 1 Mi.</summary>
    public int MaxCellTextCharacters { get; set; } = 1024 * 1024;

    /// <summary>Gets or sets the optional callback. The reader does not retain an accumulated diagnostic list.</summary>
    public Action<ReaderDiagnostic>? DiagnosticSink { get; set; }

    /// <summary>Captures validated limits and culture.</summary>
    /// <returns>The resulting value.</returns>
    internal OpenXmlReaderOptions Snapshot()
    {
        if (MaxCharactersInPart <= 0 || MaxSharedStringItems <= 0 || MaxSharedStringCharacters <= 0 || MaxStyleRecords <= 0 || MaxCellTextCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OpenXmlReaderOptions), "Reader limits must be positive.");
        }

        return new OpenXmlReaderOptions
        {
            SelectedSheetNames = SelectedSheetNames == null ? null : new List<string>(SelectedSheetNames).AsReadOnly(),
            Culture = CultureInfo.ReadOnly((CultureInfo)(Culture ?? CultureInfo.CurrentCulture).Clone()),
            MaxCharactersInPart = MaxCharactersInPart,
            MaxSharedStringItems = MaxSharedStringItems,
            MaxSharedStringCharacters = MaxSharedStringCharacters,
            MaxStyleRecords = MaxStyleRecords,
            MaxCellTextCharacters = MaxCellTextCharacters,
            DiagnosticSink = DiagnosticSink,
        };
    }
}
