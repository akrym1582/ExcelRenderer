using ExcelRenderer.OpenXml.Reader.Model.Workbook;
using static ExcelRenderer.Core.Excel.OpenXmlStyleConverter;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Fixed three-icon classification, preserving saved boundary inclusivity.</summary>
internal sealed partial class ConditionalFormattingSession
{
    private sealed class IconSetRule
    {
        private readonly string set;
        private readonly bool show;
        private readonly bool reverse;
        private readonly bool[] inclusive;

        internal IconSetRule(ElementInfo rule, Formulas.Evaluation.FormulaEvaluationOptions? options)
        {
            var icons = Child(rule, "iconSet") ?? throw AttributesInvalid("Missing iconSet.");
            if (icons.NamespaceUri != rule.NamespaceUri || rule.Children.Count(child => child.Name == "iconSet") != 1)
            {
                throw AttributesInvalid("Invalid iconSet namespace or duplicate payload.");
            }

            set = Attribute(icons, "iconSet") ?? "3TrafficLights1";
            if (set is not ("3Arrows" or "3ArrowsGray" or "3TrafficLights1" or "3Symbols" or "3Symbols2") || VisualBoolean(icons, "custom", false) || icons.Children.Any(child => child.Name == "cfIcon"))
            {
                throw new ConditionalFormattingException("ConditionalFormattingIconSetUnsupported", "Only the five fixed three-icon sets are supported.");
            }

            show = VisualBoolean(icons, "showValue", true);
            reverse = VisualBoolean(icons, "reverse", false);
            var points = icons.Children.Where(child => child.Name == "cfvo" && child.NamespaceUri == icons.NamespaceUri).ToArray();
            if (points.Length != 3)
            {
                throw AttributesInvalid("Three icon thresholds are required.");
            }

            inclusive = points.Select(point => VisualBoolean(point, "gte", true)).ToArray();
            Thresholds = new(points, options, icons.NamespaceUri == ConditionalFormattingRuleNormalizer.X14, true, true);
        }

        internal ConditionalFormattingThresholdResolver Thresholds { get; }

        internal ConditionalIcon Evaluate(double value, bool rightToLeft)
        {
            var index = Compare(2) ? 2 : Compare(1) ? 1 : 0;
            return new(set, reverse ? 2 - index : index, show, rightToLeft);

            bool Compare(int boundary) => inclusive[boundary] ? value >= Thresholds.Values[boundary] : value > Thresholds.Values[boundary];
        }
    }
}
