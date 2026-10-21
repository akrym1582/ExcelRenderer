using System.Globalization;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.Values;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.OpenXml.Reader.Model.ConditionalFormatting;
using static ExcelRenderer.Core.Excel.OpenXmlStyleConverter;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Prepares population thresholds once, separately from per-cell matching.</summary>
internal sealed partial class ConditionalFormattingSession
{
    private sealed class PopulationRule
    {
        private readonly string type;
        private readonly int rank;
        private readonly bool bottom;
        private readonly bool percent;
        private readonly bool above;
        private readonly bool equal;
        private readonly int deviations;
        private readonly ConditionalColorScale? scale;
        private readonly DataBarRule? bar;
        private readonly IconSetRule? icons;
        private readonly ConditionalFormattingRuleInfo source;
        private double threshold;

        internal PopulationRule(ConditionalFormattingRuleInfo source, FormulaEvaluationOptions? options)
        {
            this.source = source;
            type = Attribute(source.Source, "type")!;
            switch (type)
            {
                case "top10":
                    percent = Boolean("percent", false);
                    bottom = Boolean("bottom", false);
                    rank = Integer("rank", 10, 1, percent ? 100 : 1000);
                    break;
                case "aboveAverage":
                    above = Boolean("aboveAverage", true);
                    equal = Boolean("equalAverage", false);
                    deviations = Integer("stdDev", 0, 0, 3);
                    break;
                case "dataBar":
                case "iconSet":
                    ValidateVisualSource(source.Source);
                    if (source.Extension is { } extension)
                    {
                        ValidateVisualSource(extension.Source);
                    }

                    if (type == "dataBar")
                    {
                        bar = new(source.Source, options, source.Extension is not null || source.Source.NamespaceUri == ConditionalFormattingRuleNormalizer.X14);
                    }
                    else
                    {
                        icons = new(source.Source, options);
                    }

                    break;
                case "colorScale":
                    if (Boolean("stopIfTrue", false) || Attribute(source.Source, "dxfId") is not null)
                    {
                        throw new ConditionalFormattingException("ConditionalFormattingAttributesInvalid", "colorScale cannot combine stopIfTrue or a differential style.");
                    }

                    scale = new(source.Source, options);
                    break;
            }
        }

        internal bool Gradient => bar?.Gradient ?? false;

        internal ConditionalPopulation? Population { get; private set; }

        internal Exception? Failure { get; set; }

        internal void Initialize(ConditionalPopulation population, WorkbookCalculationSession calculation, string sheet, CancellationToken token)
        {
            if (type is "top10" or "aboveAverage" or "colorScale" or "dataBar" or "iconSet")
            {
                population.PrepareNumbers(type == "top10" || (scale?.NeedsSort ?? bar?.Thresholds.NeedsSort ?? icons?.Thresholds.NeedsSort ?? false), token);
                var count = population.Numbers.Count;
                if (count > 0)
                {
                    if (type == "top10")
                    {
                        var take = Math.Min(count, percent ? (int)Math.Ceiling(count * (double)rank / 100) : rank);
                        threshold = population.Numbers[bottom ? take - 1 : count - take];
                    }
                    else if (type == "aboveAverage")
                    {
                        var statistics = population.Statistics(token);
                        threshold = statistics.Mean + ((above ? 1 : -1) * deviations * statistics.Sigma);
                        if (!ConditionalPopulation.Finite(threshold))
                        {
                            throw new ConditionalFormattingException("ConditionalFormattingThresholdInvalid", "Nonfinite average threshold.");
                        }
                    }
                }

                scale?.Initialize(population, calculation, sheet, source.Ranges[0].First, token);
                bar?.Thresholds.Initialize(population, calculation, sheet, source.Ranges[0].First, token);
                icons?.Thresholds.Initialize(population, calculation, sheet, source.Ranges[0].First, token);
            }

            Population = population;
        }

        internal Evaluation Evaluate(FormulaValue value, CancellationToken token, bool rightToLeft)
        {
            if (type is "duplicateValues" or "uniqueValues")
            {
                var frequency = Population!.Frequency(value, token);
                return new(type == "duplicateValues" ? frequency >= 2 : frequency == 1, null);
            }

            if (value.Kind != FormulaValueKind.Number || Population!.Numbers.Count == 0)
            {
                return default;
            }

            if (bar is not null)
            {
                return new(true, null, bar.Evaluate(value.Number, rightToLeft));
            }

            if (icons is not null)
            {
                return new(true, null, Icon: icons.Evaluate(value.Number, rightToLeft));
            }

            if (scale is not null)
            {
                return new(true, scale.Color(value.Number));
            }

            var match = type == "top10" ? bottom ? value.Number <= threshold : value.Number >= threshold :
                above ? equal ? value.Number >= threshold : value.Number > threshold : equal ? value.Number <= threshold : value.Number < threshold;
            return new(match, null);
        }

        private static void ValidateVisualSource(OpenXml.Reader.Model.Workbook.ElementInfo source)
        {
            if (VisualBoolean(source, "stopIfTrue", false) || Attribute(source, "dxfId") is not null || VisualBoolean(source, "activePresent", false) ||
                source.Children.Any(child => child.Name is "dxf" or "f" || (child.Name is not ("dataBar" or "iconSet" or "extLst"))))
            {
                throw AttributesInvalid("Visual rules with differential styles, stopIfTrue or active formulas are unsupported.");
            }
        }

        private bool Boolean(string name, bool fallback) => Attribute(source.Source, name) switch
        {
            null => fallback,
            "0" or "false" => false,
            "1" or "true" => true,
            _ => throw new ConditionalFormattingException("ConditionalFormattingAttributesInvalid", "Invalid Boolean attribute: " + name),
        };

        private int Integer(string name, int fallback, int min, int max)
        {
            var text = Attribute(source.Source, name);
            if (text is null)
            {
                return fallback;
            }

            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < min || number > max)
            {
                throw new ConditionalFormattingException("ConditionalFormattingAttributesInvalid", "Invalid integer attribute: " + name);
            }

            return number;
        }
    }
}
