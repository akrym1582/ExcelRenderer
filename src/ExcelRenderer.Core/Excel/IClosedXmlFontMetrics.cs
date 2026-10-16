using ClosedXML.Graphics;

namespace ExcelRenderer.Core.Excel;

/// <summary>Legacy loading and width hooks used only by the ClosedXML adapter.</summary>
internal interface IClosedXmlFontMetrics
{
    /// <summary>Gets the workbook graphic engine, or the existing ClosedXML default.</summary>
    IXLGraphicEngine? GraphicEngine { get; }

    /// <summary>Resolves the legacy width when XML setup metadata is absent.</summary>
    /// <param name="width">The ClosedXML column width.</param>
    /// <returns>A compatibility width, or null to use source metadata arithmetic.</returns>
    double? DefaultColumnWidth(double width);
}
