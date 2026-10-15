using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Abstractions;

/// <summary>Optional internal capability for product-owned text payloads.</summary>
internal interface IExtendedTextLayoutService
{
    /// <summary>Measures opaque cell text metadata when supported by the product.</summary>
    /// <param name="cell">The source cell with optional metadata.</param>
    /// <param name="availableWidth">The available width.</param>
    /// <param name="wrap">Whether to wrap.</param>
    /// <returns>A finalized layout, or null for the ordinary cell path.</returns>
    TextLayoutResult? Layout(ReportCell cell, double availableWidth, bool wrap);
}
