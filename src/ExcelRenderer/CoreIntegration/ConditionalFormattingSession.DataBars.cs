using System.Globalization;
using ExcelRenderer.Core.Model;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;
using static ExcelRenderer.Core.Excel.OpenXmlStyleConverter;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Phase 3 data bar input validation and normalized interval profile.</summary>
internal sealed partial class ConditionalFormattingSession
{
    private static bool VisualBoolean(ElementInfo source, string name, bool fallback) => Attribute(source, name) switch
    {
        null => fallback,
        "0" or "false" => false,
        "1" or "true" => true,
        _ => throw AttributesInvalid("Invalid Boolean: " + name),
    };

    private static ConditionalFormattingException AttributesInvalid(string message) => new("ConditionalFormattingAttributesInvalid", message);

    private sealed class DataBarRule
    {
        private readonly bool extension;
        private readonly bool show;
        private readonly bool border;
        private readonly bool negativeSame;
        private readonly bool negativeBorderSame;
        private readonly double minimum;
        private readonly double maximum;
        private readonly string direction;
        private readonly string axis;
        private readonly ReportColor fill;
        private readonly ReportColor? borderColor;
        private readonly ReportColor? negativeFill;
        private readonly ReportColor? negativeBorder;
        private readonly ReportColor? axisColor;

        internal DataBarRule(ElementInfo rule, Formulas.Evaluation.FormulaEvaluationOptions? options, bool extended)
        {
            var bar = Child(rule, "dataBar") ?? throw AttributesInvalid("Missing dataBar.");
            extension = extended;
            if (bar.NamespaceUri != (extension ? ConditionalFormattingRuleNormalizer.X14 : ConditionalFormattingRuleNormalizer.Standard) || rule.Children.Count(child => child.Name == "dataBar") != 1)
            {
                throw AttributesInvalid("Invalid dataBar namespace or duplicate payload.");
            }

            show = VisualBoolean(bar, "showValue", true);
            border = VisualBoolean(bar, "border", false);
            Gradient = !extension || VisualBoolean(bar, "gradient", true);
            negativeSame = VisualBoolean(bar, "negativeBarColorSameAsPositive", false);
            negativeBorderSame = VisualBoolean(bar, "negativeBarBorderColorSameAsPositive", true);
            minimum = Length("minLength", 10) / 100;
            maximum = Length("maxLength", 90) / 100;
            if (minimum > maximum)
            {
                throw AttributesInvalid("Descending data bar lengths.");
            }

            direction = Attribute(bar, "direction") ?? "context";
            axis = extension ? Attribute(bar, "axisPosition") ?? "automatic" : "none";
            if (direction is not ("context" or "leftToRight" or "rightToLeft") || axis is not ("none" or "automatic" or "middle"))
            {
                throw AttributesInvalid("Invalid bar direction or axis.");
            }

            fill = Color(Child(bar, "color") is not null ? "color" : "fillColor", true)!.Value;
            borderColor = Color("borderColor", border);
            negativeFill = Color("negativeFillColor", extension && !negativeSame);
            negativeBorder = Color("negativeBorderColor", extension && border && !negativeBorderSame);
            axisColor = Color("axisColor", extension && axis != "none");
            var points = bar.Children.Where(child => child.Name == "cfvo" && child.NamespaceUri == bar.NamespaceUri).ToArray();
            if (points.Length != 2)
            {
                throw AttributesInvalid("A data bar requires two thresholds.");
            }

            Thresholds = new(points, options, extension, strictArithmetic: true);

            ReportColor? Color(string name, bool required) => ConditionalStyle.ResolvedColor(bar.Children.FirstOrDefault(child => child.Name == name && child.NamespaceUri == (name == "color" ? ConditionalFormattingRuleNormalizer.Standard : ConditionalFormattingRuleNormalizer.X14))) is { } color ? color : required ? throw AttributesInvalid("Missing or unresolved " + name) : null;
            double Length(string name, double fallback) => Attribute(bar, name) is not { } text ? fallback : double.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0 && value <= 100 ? value : throw AttributesInvalid("Invalid " + name);
        }

        internal ConditionalFormattingThresholdResolver Thresholds { get; }

        internal bool Gradient { get; }

        internal ConditionalDataBar Evaluate(double value, bool sheetRtl)
        {
            var lower = Thresholds.Values[0];
            var upper = Thresholds.Values[1];
            var span = upper - lower;
            if (!ConditionalPopulation.Finite(span) || lower > upper || (lower == upper && !(extension && lower == 0 && axis != "none")))
            {
                throw new ConditionalFormattingException("ConditionalFormattingThresholdInvalid", "Collapsed or nonfinite bar interval.");
            }

            double start = 0;
            double end = 0;
            double? line = null;
            if (extension && axis == "middle")
            {
                var magnitude = Math.Max(Math.Abs(lower), Math.Abs(upper));
                var length = magnitude == 0 || value == 0 ? 0 : 0.5 * Length(Math.Abs(value) / magnitude);
                start = value < 0 ? 0.5 - length : 0.5;
                end = value > 0 ? 0.5 + length : 0.5;
                line = 0.5;
            }
            else if (extension && axis == "automatic")
            {
                var origin = lower >= 0 ? 0 : upper <= 0 ? 1 : -lower / span;
                start = end = origin;
                if (lower != upper)
                {
                    if (lower >= 0 && value > 0)
                    {
                        end = Length((value - lower) / span);
                    }
                    else if (upper <= 0 && value < 0)
                    {
                        start = 1 - Length((upper - value) / span);
                    }
                    else if (lower < 0 && upper > 0)
                    {
                        if (value > 0)
                        {
                            end = origin + ((1 - origin) * Length(value / upper));
                        }
                        else if (value < 0)
                        {
                            start = origin - (origin * Length(value / lower));
                        }
                    }
                }

                line = origin > 0 && origin < 1 ? origin : null;
            }
            else
            {
                end = Length((value - lower) / span);
            }

            if (extension && (direction == "rightToLeft" || (direction == "context" && sheetRtl)))
            {
                (start, end) = (1 - end, 1 - start);
                line = line is { } position ? 1 - position : null;
            }

            var negative = extension && value < 0;
            return new(start, end, line, negative && !negativeSame ? negativeFill!.Value : fill, border ? negative && !negativeBorderSame ? negativeBorder : borderColor : null, axisColor, show);

            double Length(double ratio) => ConditionalPopulation.Finite(ratio) ? minimum + (Math.Max(0, Math.Min(1, ratio)) * (maximum - minimum)) : throw new ConditionalFormattingException("ConditionalFormattingThresholdInvalid", "Nonfinite bar interpolation.");
        }
    }
}
