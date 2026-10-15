using ExcelRenderer.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Owns an immutable snapshot of full-product partial cell formatting.</summary>
/// <param name="Runs">The effective partial formatting.</param>
internal sealed record FullCellTextData(IReadOnlyList<CellTextRun> Runs) : Core.Extensibility.ICoreExtensionData
{
    /// <summary>Gets a value indicating whether unsupported partial decorations were observed.</summary>
    internal bool UnsupportedFormatting { get; init; }

    /// <summary>Validates and snapshots a public model without copying its cell dictionary.</summary>
    /// <param name="cell">The public source cell.</param>
    /// <returns>The immutable payload, or null for ordinary text.</returns>
    internal static FullCellTextData? Create(ReportCell cell)
    {
        if (cell.RichTextRuns is not { } runs)
        {
            return null;
        }

        var snapshot = runs.ToArray();
        if (snapshot.Any(run => run is null || run.Text is null || run.Font is null ||
            !double.IsFinite(run.Font.Size) || run.Font.Size <= 0) ||
            string.Concat(snapshot.Select(run => run.Text)) != cell.Text)
        {
            throw new ArgumentException("Rich text must match cell text and have valid font sizes and non-null runs.", nameof(cell));
        }

        return new(Array.AsReadOnly(snapshot.Where(run => run.Text.Length > 0).ToArray())) { UnsupportedFormatting = cell.UnsupportedRichTextFormatting };
    }
}
