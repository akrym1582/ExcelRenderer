using ClosedXML.Graphics;

namespace ExcelRenderer.Core.Excel;

/// <summary>Supplies product font resolution without changing width conversion arithmetic.</summary>
internal interface ICoreFontMetrics
{
    /// <summary>Gets the workbook graphic engine, or the existing ClosedXML default.</summary>
    IXLGraphicEngine? GraphicEngine { get; }

    /// <summary>Gets the default Normal style used as a cache key.</summary>
    NormalFontMetadata DefaultFont { get; }

    /// <summary>Measures the Normal font or applies the product fallback and diagnostics.</summary>
    /// <param name="normalFont">The original workbook Normal font.</param>
    /// <param name="sheetName">The diagnostic sheet.</param>
    /// <returns>The rounded maximum digit width in pixels.</returns>
    double MaximumDigitWidth(NormalFontMetadata? normalFont, string sheetName);

    /// <summary>Resolves the legacy width when XML setup metadata is absent.</summary>
    /// <param name="width">The ClosedXML column width.</param>
    /// <returns>A product compatibility width, or null to use common metadata arithmetic.</returns>
    double? DefaultColumnWidth(double width);
}
