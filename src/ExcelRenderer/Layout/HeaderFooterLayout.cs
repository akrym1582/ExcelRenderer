using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Expands and positions the header and footer for a finalized page number.</summary>
internal static class HeaderFooterLayout
{
    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <returns>The calculated value.</returns>
    /// <param name="sheet">The sheet value.</param>
    /// <param name="pageNumber">The pageNumber value.</param>
    /// <param name="pageCount">The pageCount value.</param>
    /// <param name="timestamp">An optional conversion clock snapshot for repeatable date/time fields.</param>
    internal static IReadOnlyList<RenderText> Create(ReportSheet sheet, int pageNumber, int pageCount, DateTime? timestamp = null)
    {
        if (sheet.HeaderFooter is not { } header)
        {
            return [];
        }

        var metadata = new Core.Model.ReportSheet(sheet.Name, new Dictionary<Core.Model.CellAddress, Core.Model.ReportCell>(), new Dictionary<int, Core.Model.ColumnDefinition>(), new Dictionary<int, Core.Model.RowDefinition>(), [], CoreIntegration.CoreModelAdapter.ToCore(sheet.PageSettings))
        {
            HeaderFooter = CoreIntegration.CoreModelAdapter.ToCoreHeader(header),
        };
        return Core.Layout.HeaderFooterLayout.Create(metadata, pageNumber, pageCount, timestamp, CoreIntegration.CoreModelAdapter.ToCore(CellStyle.Default))
            .Select(text => new RenderText(CoreIntegration.CoreModelAdapter.ToPublic(text.Bounds), text.Text, CoreIntegration.CoreModelAdapter.ToPublic(text.Style))).ToArray();
    }
}
