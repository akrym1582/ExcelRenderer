using ExcelRenderer.OpenXml.Reader.Model.ConditionalFormatting;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;
using static ExcelRenderer.Core.Excel.OpenXmlStyleConverter;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Joins priority-less x14 data bars before priority validation.</summary>
internal static class ConditionalFormattingRuleNormalizer
{
    /// <summary>Standard spreadsheet namespace.</summary>
    internal const string Standard = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>Excel 2010 conditional-format extension namespace.</summary>
    internal const string X14 = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    /// <summary>Excel formula and sqref payload namespace.</summary>
    internal const string Xm = "http://schemas.microsoft.com/office/excel/2006/main";

    /// <summary>Combines linked data bars before sorting and validating worksheet priorities.</summary>
    /// <param name="rules">Bounded immutable source rules.</param>
    /// <param name="token">Cooperative cancellation.</param>
    /// <returns>Normalized logical rules and link failures.</returns>
    internal static IEnumerable<(ConditionalFormattingRuleInfo Source, string? Failure)> Normalize(IReadOnlyList<ConditionalFormattingRuleInfo> rules, CancellationToken token)
    {
        if (!rules.Any(rule => rule.IsExtension))
        {
            foreach (var ordinary in rules)
            {
                token.ThrowIfCancellationRequested();
                yield return (ordinary, null);
            }

            yield break;
        }

        var bases = rules.Where(rule => rule.Source.NamespaceUri == Standard).Select(rule => (Rule: rule, Id: Link(rule.Source))).Where(item => item.Id is not null).GroupBy(item => item.Id!.Value).ToDictionary(group => group.Key, group => group.Select(item => item.Rule).ToArray());
        var extensions = rules.Where(rule => rule.Source.NamespaceUri == X14 && Attribute(rule.Source, "priority") is null && Id(Attribute(rule.Source, "id")) is not null).GroupBy(rule => Id(Attribute(rule.Source, "id"))!.Value).ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var rule in rules)
        {
            token.ThrowIfCancellationRequested();
            if (rule.Source.NamespaceUri == X14 && Attribute(rule.Source, "priority") is null)
            {
                var id = Id(Attribute(rule.Source, "id"));
                if (id is null || !bases.ContainsKey(id.Value))
                {
                    yield return (rule, "ConditionalFormattingExtensionLinkInvalid");
                }

                continue;
            }

            var identifiers = Descendants(rule.Source).Where(item => item.Name == "id" && item.NamespaceUri == X14).Take(2).ToArray();
            if (identifiers.Length > 1 || (identifiers.Length == 1 && Id(identifiers[0].Text) is null))
            {
                yield return (rule, "ConditionalFormattingExtensionLinkInvalid");
                continue;
            }

            var link = Link(rule.Source);
            if (link is { } guid && extensions.TryGetValue(guid, out var linked))
            {
                var extension = linked[0];
                var valid = bases[guid].Length == 1 && linked.Length == 1 && Attribute(rule.Source, "type") == "dataBar" && Attribute(extension.Source, "type") == "dataBar" &&
                    EquivalentRanges(rule, extension, token);
                if (!valid)
                {
                    yield return (rule, "ConditionalFormattingExtensionLinkInvalid");
                    continue;
                }

                var visual = Child(extension.Source, "dataBar");
                var color = Child(Child(rule.Source, "dataBar"), "color");
                if (visual is null || color is null || Child(visual, "fillColor") is not null)
                {
                    yield return (rule, "ConditionalFormattingAttributesInvalid");
                    continue;
                }

                visual = new(visual.Name, visual.Text, visual.Attributes, Array.AsReadOnly(visual.Children.Concat(new[] { color }).ToArray()), visual.NamespaceUri);
                var source = new ElementInfo(rule.Source.Name, rule.Source.Text, rule.Source.Attributes, Array.AsReadOnly(new[] { visual }), rule.Source.NamespaceUri);
                yield return (new(rule.Ranges, source, rule.Style, true, new(extension.Source, guid)), UnknownExtension(rule.Source) || UnknownExtension(extension.Source) ? "ConditionalFormattingExtensionUnsupported" : null);
                continue;
            }

            yield return (rule, null);
        }
    }

    /// <summary>Tests whether a required extension differs from the supported legacy link marker.</summary>
    /// <returns>Whether unsupported required extension metadata is present.</returns>
    /// <param name="source">Original rule metadata.</param>
    internal static bool UnknownExtension(ElementInfo source) => Descendants(source).Any(item => item.Name == "extLst" && item.NamespaceUri != Standard) ||
        Descendants(source).Any(item => item.Name == "ext" && !(item.NamespaceUri == Standard && Guid.TryParse(Attribute(item, "uri"), out var uri) && uri == new Guid("B025F937-C7B1-47D3-B67F-A62EFF666E3E") && item.Children.Count == 1 && item.Children[0].Name == "id" && item.Children[0].NamespaceUri == X14));

    private static bool EquivalentRanges(ConditionalFormattingRuleInfo first, ConditionalFormattingRuleInfo second, CancellationToken token)
    {
        try
        {
            return ConditionalPopulation.Normalize(first.Ranges, token).Key == ConditionalPopulation.Normalize(second.Ranges, token).Key;
        }
        catch (ConditionalFormattingException)
        {
            return false;
        }
    }

    private static Guid? Link(ElementInfo source) => Id(Descendants(source).FirstOrDefault(item => item.Name == "id" && item.NamespaceUri == X14)?.Text);

    private static Guid? Id(string? text) => Guid.TryParse(text, out var id) ? id : null;

    private static IEnumerable<ElementInfo> Descendants(ElementInfo source) => source.Children.SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
