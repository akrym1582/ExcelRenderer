namespace ExcelRenderer.Tool.Commands;

/// <summary>Aggregates CLI observations according to their producer's reporting contract.</summary>
internal sealed class MetricSummary
{
    private static readonly HashSet<string> Totals = new(StringComparer.Ordinal)
    {
        "commands", "borderCandidates", "buffer.spillCount", "conditionalFormatting.populations", "conditionalFormatting.sorts",
        "conditionalFormatting.valueReads", "conditionalFormatting.rulePrioritySorts",
        "conditionalFormatting.ruleRowPlans", "conditionalFormatting.ruleListSearches", "conditionalFormatting.ruleListSorts",
        "coreBorderGeometry", "coreCommandBuild", "coreGeometryPlan", "corePageBuild", "coreTextPageBuild",
        "fontFaceExtracted", "fontRunCompletions", "fontSnapshotRead", "fontsBytesLoaded",
        "glyphClusterElementPasses", "glyphInkCacheHits", "glyphInkComputations", "glyphRunsFinalized",
        "glyphStorageCopiedGlyphs", "glyphStorageCopies", "imageAlphaCacheHits", "imageAlphaScans",
        "imageCacheHit", "imageDecode", "imagePixelDecode", "layoutCacheEviction", "layoutCacheHit", "layoutCacheMiss",
        "legacyTextPaints", "lineMetricsComputed", "mainFullPageBuilds", "mainTextPreflightBuilds",
        "openXmlReaderWorkbook", "outlineCompatibilityParagraphs", "outlineIntermediateGlyphCopies", "outlineParagraphs",
        "pageCandidateSort", "pageCellDeduplicateSort", "pageCellIndexBuild", "paintDisposed", "pathEffectDisposed",
        "pdfFontCreated", "pdfFontDisposed", "pdfFontRunsCollected", "pdfOriginalFontBytes", "pdfPositionsWritten",
        "pdfSave", "pdfSubsetFontBytes", "pdfSubsetTypefaceCreated", "pdfSubsetTypefaceDisposed",
        "postScript.fontBytes", "postScript.spillCount", "resolvedBorderSegments", "supportCacheHits", "supportChecks",
        "svg.spillCount", "svgBufferBytes", "textGlyphCalls", "textGlyphCharacters", "textWrapComparisons", "typefaceCreated",
        "typefaceDisposed", "unfinalizedTextLayouts",
    };

    private static readonly HashSet<string> Peaks = new(StringComparer.Ordinal)
    {
        "imageCacheEstimatedBytes", "layoutCacheEntries", "layoutCacheRetainedUnits", "pagePayloadMax",
        "pngBitmapBytes", "postScript.bufferMemoryPeakBytes", "svgBufferMemoryPeakBytes",
    };

    private static readonly HashSet<string> Snapshots = new(StringComparer.Ordinal)
    {
        "cells", "model.cells", "modelSelectedCells", "renderCells", "pageCandidateCount", "pdfBackend",
        "readerStyleCacheEntries", "readerStyleCacheHits", "readerStyleConversions", "postScript.bytes", "postScript.fontCount",
        "conditionalFormatting.evaluations", "conditionalFormatting.candidateVisits", "conditionalFormatting.candidates",
        "conditionalFormatting.ruleNormalizationWork", "conditionalFormatting.retainedRuleReferences",
        "conditionalFormatting.displayCandidateVisits", "conditionalFormatting.candidateRangeWork", "conditionalFormatting.limitTargetProbes",
        "managedAllocatedBytes",
    };

    private readonly Dictionary<string, double> values = new(StringComparer.Ordinal);
    private readonly object gate = new();

    /// <summary>Records an event total, observed peak or final snapshot without retaining conversion objects.</summary>
    /// <param name="name">The metric name.</param>
    /// <param name="value">The observed value.</param>
    internal void Report(string name, double value)
    {
        lock (gate)
        {
            if (name == "pagePayloadActive")
            {
                value += values.GetValueOrDefault(name);
                values["pagePayloadMax"] = Math.Max(values.GetValueOrDefault("pagePayloadMax"), value);
            }
            else if (name.EndsWith(".ms", StringComparison.Ordinal) || Totals.Contains(name) || (IsCacheEvent(name) && !Snapshots.Contains(name)))
            {
                value += values.GetValueOrDefault(name);
            }
            else if (name.EndsWith(".max", StringComparison.Ordinal) || Peaks.Contains(name))
            {
                value = values.TryGetValue(name, out var previous) ? Math.Max(previous, value) : value;
            }

            // .current, producer snapshots and unknown future metrics retain their last value.
            values[name] = value;
        }
    }

    /// <summary>Returns a stable, sorted copy for output after conversion completes.</summary>
    /// <returns>The observations in ordinal name order.</returns>
    internal KeyValuePair<string, double>[] Snapshot()
    {
        lock (gate)
        {
            return values.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        }
    }

    private static bool IsCacheEvent(string name) => name.Contains("Cache", StringComparison.Ordinal) &&
        (name.EndsWith("Hits", StringComparison.Ordinal) || name.EndsWith("Misses", StringComparison.Ordinal) ||
         name.EndsWith("Evictions", StringComparison.Ordinal) || name.EndsWith("Scans", StringComparison.Ordinal) ||
         name.EndsWith("Bypasses", StringComparison.Ordinal));
}
