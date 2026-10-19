using ExcelRenderer.Core.Model;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Connects lazy population rules to the shared snapshot and existing priority pass.</summary>
internal sealed partial class ConditionalFormattingSession
{
    private readonly Dictionary<string, ConditionalPopulation> populations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Exception> populationFailures = new(StringComparer.Ordinal);
    private long populationVisits;
    private long normalizedRectangles;

    private Evaluation Evaluate(PreparedRule rule, OpenXml.Reader.Model.Cells.CellAddress address, CancellationToken token)
    {
        if (rule.PopulationRule is not { } evaluator)
        {
            return new(Matches(rule, address), null);
        }

        if (evaluator.Failure is { } failure)
        {
            throw failure;
        }

        try
        {
            if (evaluator.Population is null)
            {
                var normalized = ConditionalPopulation.Normalize(rule.Source.Ranges, token);
                if (populationFailures.TryGetValue(normalized.Key, out var previous))
                {
                    throw previous;
                }

                if (!populations.TryGetValue(normalized.Key, out var population))
                {
                    try
                    {
                        normalizedRectangles = checked(normalizedRectangles + normalized.Ranges.Count);
                        if (normalizedRectangles > ConditionalPopulation.NormalizedRangeLimit)
                        {
                            throw new ConditionalFormattingException("ConditionalFormattingLimitExceeded", "Retained population rectangles exceed 65536 per sheet.");
                        }

                        population = ConditionalPopulation.Create(normalized.Ranges, calculation!, info.Sheet.Name, Visit, token);
                        populations.Add(normalized.Key, population);
                    }
                    catch (Exception exception) when (exception is ConditionalFormattingException or FormulaCalculationException)
                    {
                        populationFailures.Add(normalized.Key, exception);
                        throw;
                    }
                }

                evaluator.Initialize(population, calculation!, info.Sheet.Name, token);
            }

            return evaluator.Evaluate(calculation!.ReadValue(new(info.Sheet.Name, address.Row, address.Column)), token);
        }
        catch (Exception exception) when (exception is ConditionalFormattingException or FormulaCalculationException)
        {
            evaluator.Failure = exception;
            throw;
        }

        void Visit()
        {
            if (++populationVisits > ConditionalPopulation.StoredCellLimit)
            {
                throw new ConditionalFormattingException("ConditionalFormattingLimitExceeded", "Population visits/retained values exceed 2000000 per sheet.");
            }

            Core.Rendering.ConversionMetrics.Report("conditionalFormatting.valueReads", 1);
        }
    }

    private void ReportFailure(PreparedRule rule, OpenXml.Reader.Model.Cells.CellAddress address, string code, string message)
    {
        var key = (rule.Order, code);
        if (diagnostics.TryGetValue(key, out var previous))
        {
            diagnostics[key] = previous with { OccurrenceCount = previous.OccurrenceCount + 1 };
        }
        else
        {
            diagnostics.Add(key, new(code, DiagnosticSeverity.Warning, DiagnosticStage.Read, "The condition could not be evaluated; ordinary formatting is retained. " + message, info.Sheet.Name, address.ToString(), ObjectId: Id(rule)));
        }
    }

    private readonly record struct Evaluation(bool Match, ReportColor? Background);
}
