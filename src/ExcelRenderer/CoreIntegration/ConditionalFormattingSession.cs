using System.Globalization;
using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.OpenXml.Reader.Model.ConditionalFormatting;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;
using ExcelRenderer.Rendering;
using static ExcelRenderer.Core.Excel.OpenXmlStyleConverter;
using SourceRange = ExcelRenderer.OpenXml.Reader.Model.Cells.CellRange;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Owns the main product's rule evaluation, priority composition and bounded candidates for one sheet.</summary>
internal sealed partial class ConditionalFormattingSession
{
    private static readonly IReadOnlyDictionary<string, ElementInfo> EmptyProperties = new Dictionary<string, ElementInfo>();
    private readonly WorksheetInfo info;
    private readonly WorkbookCalculationSession? calculation;
    private readonly Func<OpenXml.Reader.Model.Cells.CellAddress, bool> isTarget;
    private readonly PreparedRule[] rules;
    private readonly Dictionary<(int Rule, string Code), ConversionDiagnostic> diagnostics = [];
    private readonly bool enabled;
    private readonly Excel.ExcelReadOptions options;
    private ConditionalTargetSearch? targetSearch;
    private Core.Layout.BandIndex<(PreparedRule Rule, SourceRange Range)> index = new([]);
    private long evaluations;
    private CancellationToken cancellationToken;
    private IEnumerable<CellRange> limitRegions = [];

    /// <summary>Initializes a new instance of the <see cref="ConditionalFormattingSession"/> class.</summary>
    /// <param name="info">Saved sheet rules and geometry.</param>
    /// <param name="calculation">Shared dependency snapshot, absent when disabled.</param>
    /// <param name="options">Main product read policy.</param>
    /// <param name="isTarget">Exact selected visible cell policy.</param>
    /// <param name="token">Cooperative cancellation.</param>
    internal ConditionalFormattingSession(WorksheetInfo info, WorkbookCalculationSession? calculation, Excel.ExcelReadOptions options, Func<OpenXml.Reader.Model.Cells.CellAddress, bool> isTarget, CancellationToken token = default)
    {
        this.info = info;
        this.calculation = calculation;
        this.isTarget = isTarget;
        this.options = options;
        enabled = options.ApplyConditionalFormatting;
        var prepared = enabled ? ConditionalFormattingRuleNormalizer.Normalize(info.ConditionalFormattingRules, token).Select((entry, order) =>
        {
            var result = Prepare(entry.Source, order, options.FormulaEvaluationOptions);
            result.Failure = entry.Failure ?? result.Failure;
            return result;
        }).Where(rule =>
        {
            if (rule.Source.Source.NamespaceUri != ConditionalFormattingRuleNormalizer.X14 || Attribute(rule.Source.Source, "priority") is not null)
            {
                return true;
            }

            Features.Add(Feature(rule, "ConditionalFormattingExtensionLinkInvalid", "An extension without a priority has no unique legacy data bar owner."));
            return false;
        }) : info.ConditionalFormattingRules.Select((source, order) => new PreparedRule(source, order, int.TryParse(Attribute(source.Source, "priority"), out var priority) ? priority : int.MaxValue, false));
        rules = enabled ? prepared.OrderBy(rule => rule.Priority).ThenBy(rule => rule.Order).ToArray() : prepared.ToArray();
        var duplicates = new HashSet<PreparedRule>();
        if (enabled)
        {
            ConversionMetrics.Report("conditionalFormatting.rulePrioritySorts", 1);
            duplicates = rules.GroupBy(rule => rule.Priority).Where(group => group.Key <= 0 || group.Count() > 1).SelectMany(group => group).ToHashSet();
        }

        foreach (var rule in rules)
        {
            if (!enabled)
            {
                Features.Add(Feature(rule, "ConditionalFormattingDisabled", "Conditional formatting was intentionally disabled."));
                continue;
            }

            if (duplicates.Contains(rule) || rule.Priority == int.MaxValue)
            {
                rule.Failure ??= "ConditionalFormattingPriorityInvalid";
            }

            if (rule.Failure is { } failure)
            {
                Features.Add(Feature(rule, failure, "The saved rule cannot be evaluated reliably; affected cells retain ordinary formatting."));
            }

            if (rule.Style.HasUnsupportedProperties)
            {
                Features.Add(Feature(rule, "ConditionalFormattingStyleUnsupported", "Only supported differential properties are applied; unsupported properties reserve their priority."));
            }
        }
    }

    /// <summary>Gets stable source-rule diagnostics, deferred through the existing output selection filter.</summary>
    internal List<UnsupportedFeature> Features { get; } = [];

    /// <summary>Gets aggregated evaluation failures, with one representative cell per rule and code.</summary>
    internal IReadOnlyList<ConversionDiagnostic> Diagnostics => diagnostics.Values.ToArray();

    /// <summary>Applies appearances within existing finite regions before shared merged-border aggregation.</summary>
    /// <param name="cells">Ordinary cells already read and formatted.</param>
    /// <param name="regions">Existing finite selected, printed or used rectangles.</param>
    /// <param name="blankStyle">Ordinary inherited style for missing cells.</param>
    /// <param name="styles">Shared style interning.</param>
    /// <param name="token">Cooperative cancellation.</param>
    internal void Apply(Dictionary<CellAddress, ReportCell> cells, IReadOnlyList<CellRange> regions, Func<CellAddress, CellStyle> blankStyle, StylePool styles, CancellationToken token)
    {
        cancellationToken = token;
        if (!enabled)
        {
            return;
        }

        // Intersect first: whole-column sqrefs never create cells beyond the finite rendering geometry.
        var candidates = new HashSet<CellAddress>();
        long visits = 0;
        const long candidateLimit = 2000000;

        // An otherwise empty sheet already has one finite A1 page; formatting
        // that cell must not turn a sheet-wide sqref into a new used range.
        var bodyRegions = regions.Count == 0 ? new[] { new CellRange(new(1, 1), new(1, 1)) } : regions;
        var titleRanges = info.PrintReferences.Where(reference => reference.Name == "_xlnm.Print_Titles").SelectMany(reference => reference.Ranges).ToArray();
        var finiteRegions = bodyRegions.Concat(titleRanges.SelectMany(title => bodyRegions.Select(region => title.Kind == "Rows"
            ? new CellRange(new(title.FirstRow, region.First.Column), new(title.LastRow, region.Last.Column))
            : new CellRange(new(region.First.Row, title.FirstColumn), new(region.Last.Row, title.LastColumn)))))
            .Concat(titleRanges.Where(title => title.Kind == "Rows").SelectMany(row => titleRanges.Where(title => title.Kind == "Columns")
                .Select(column => new CellRange(new(row.FirstRow, column.FirstColumn), new(row.LastRow, column.LastColumn)))));
        limitRegions = finiteRegions;
        if (!PrepareDisplayRanges(token))
        {
            return;
        }

        if (!CollectCandidates(finiteRegions, candidates, candidateLimit, ref visits, token))
        {
            return;
        }

        var mergedIndex = new Core.Layout.BandIndex<SourceRange>(info.MergedRanges.Select(range => (range, (double)range.First.Row, (double)range.Last.Row + 1)));
        foreach (var region in finiteRegions)
        {
            foreach (var merge in mergedIndex.Query(region.First.Row, region.Last.Row + 1).Where(merge => merge.First.Column <= region.Last.Column && merge.Last.Column >= region.First.Column))
            {
                token.ThrowIfCancellationRequested();
                var rule = index.Query(merge.First.Row, merge.First.Row + 1).FirstOrDefault(entry => entry.Range.First.Column <= merge.First.Column && entry.Range.Last.Column >= merge.First.Column).Rule;
                if (rule is not null)
                {
                    if (candidates.Add(new(merge.First.Row, merge.First.Column)) && ++visits > candidateLimit)
                    {
                        ReportCandidateLimit(rule, "The finite candidate budget was exceeded; this sheet retains ordinary formatting.");
                        return;
                    }
                }
            }
        }

        var borderNeighbors = AddSavedBorderNeighbors(cells, candidates, mergedIndex, candidateLimit, ref visits, token);
        if (borderNeighbors is null)
        {
            return;
        }

        var merges = info.MergedRanges.Where(range => candidates.Contains(new(range.First.Row, range.First.Column))).ToDictionary(range => new CellAddress(range.First.Row, range.First.Column));
        if (merges.Values.Sum(range => (long)(range.Last.Row - range.First.Row + 1 + range.Last.Column - range.First.Column + 1) * 2) > candidateLimit)
        {
            ReportCandidateLimit(rules[0], "The merged-perimeter budget was exceeded; this sheet retains ordinary formatting.");
            return;
        }

        var orderedCandidates = candidates.OrderBy(address => address.Row).ThenBy(address => address.Column).ToArray();
        if (!PrepareRuleRows(orderedCandidates, token))
        {
            return;
        }

        var mergedProperties = new Dictionary<CellAddress, Dictionary<string, ElementInfo>>();
        foreach (var address in orderedCandidates)
        {
            token.ThrowIfCancellationRequested();
            var sourceAddress = new OpenXml.Reader.Model.Cells.CellAddress(address.Row, address.Column);
            if (!isTarget(sourceAddress) && !borderNeighbors.Contains(address))
            {
                continue;
            }

            Dictionary<string, ElementInfo>? properties = null;
            var uncertain = false;
            ReportColor? dynamicBackground = null;
            ConditionalDataBar? dataBar = null;
            ConditionalIcon? icon = null;
            foreach (var rule in ApplicableRules(address, token))
            {
                if (rule.Failure is not null)
                {
                    uncertain = true;
                    break;
                }

                try
                {
                    evaluations++;
                    var evaluation = Evaluate(rule, sourceAddress, token);
                    if (!evaluation.Match)
                    {
                        continue;
                    }

                    dataBar ??= evaluation.DataBar;
                    icon ??= evaluation.Icon is { } selectedIcon ? selectedIcon with { RuleId = Id(rule) } : null;
                    if (evaluation.DataBar is not null && rule.PopulationRule?.Gradient == true)
                    {
                        ReportFailure(rule, sourceAddress, "ConditionalFormattingDataBarGradientApproximated", "The gradient is approximated with a solid fill.");
                    }

                    if (evaluation.Background is { } background && properties?.ContainsKey("fill") != true)
                    {
                        dynamicBackground ??= background;
                    }
                }
                catch (ConditionalFormattingException exception)
                {
                    ReportFailure(rule, sourceAddress, exception.Code, exception.Message);
                    uncertain = true;
                    break;
                }
                catch (FormulaCalculationException exception)
                {
                    var code = exception.Message.Contains("MissingCachedValue", StringComparison.Ordinal) ? "ConditionalFormattingCachedValueMissing" :
                        exception.Message.Contains("ResourceLimitExceeded", StringComparison.Ordinal) ? "ConditionalFormattingLimitExceeded" :
                        exception.Diagnostics.Any(diagnostic => diagnostic.Code == "UnsupportedFunction") ? "ConditionalFormattingFunctionUnsupported" : "ConditionalFormattingEvaluationFailed";
                    ReportFailure(rule, sourceAddress, code, exception.Message);

                    uncertain = true;
                    break;
                }

                if (rule.Style.HasProperties)
                {
                    properties ??= new(StringComparer.Ordinal);
                    rule.Style.AddTo(properties, dynamicBackground is not null);
                }

                if (Attribute(rule.Source.Source, "stopIfTrue") is "1" or "true")
                {
                    break;
                }
            }

            if (borderNeighbors.Contains(address))
            {
                // Outside owners contribute only borders. Their font/fill and text
                // must not enlarge or otherwise change the selected content model.
                if (properties is not null)
                {
                    properties.Remove("fill");
                    properties.Remove("color");
                    properties.Remove("b");
                    properties.Remove("i");
                    properties.Remove("u");
                }

                dynamicBackground = null;
                dataBar = null;
                icon = null;
            }

            if (uncertain || ((properties?.Count ?? 0) == 0 && dynamicBackground is null && dataBar is null && icon is null))
            {
                continue;
            }

            var effectiveProperties = properties ?? EmptyProperties;
            var cell = cells.GetValueOrDefault(address) ?? new ReportCell(string.Empty, blankStyle(address));
            var style = styles.Intern(ConditionalStyle.Apply(cell.Style, effectiveProperties, dynamicBackground));
            if (!cells.ContainsKey(address) && style.Background is null && style.Border is null)
            {
                continue;
            }

            var text = FullCellPresentationData.Text(cell.ExtensionData) is { } rich ? rich with
            {
                Runs = rich.Runs.Select(run => run with { Font = CoreModelAdapter.ToPublic(ConditionalStyle.ApplyFont(CoreModelAdapter.ToCore(run.Font), effectiveProperties)) }).ToArray(),
            } : FullCellPresentationData.Text(cell.ExtensionData);
            var visualData = dataBar is null && icon is null ? null : new ConditionalCellVisual(dataBar, icon);
            cells[address] = cell with { Style = style, ExtensionData = visualData is null ? text : new FullCellPresentationData(text, visualData) };
            if (properties is not null && merges.ContainsKey(address))
            {
                mergedProperties.Add(address, properties);
            }
        }

        // A merged leader owns the appearance. Supply its explicitly conditional
        // edges at the perimeter; the shared merger still owns all aggregation.
        foreach (var entry in mergedProperties)
        {
            var range = merges[entry.Key];
            foreach (var property in entry.Value.Where(property => property.Key is "left" or "right" or "top" or "bottom"))
            {
                var edge = new Dictionary<string, ElementInfo> { [property.Key] = property.Value };
                var vertical = property.Key is "left" or "right";
                var first = vertical ? range.First.Row : range.First.Column;
                var last = vertical ? range.Last.Row : range.Last.Column;
                for (var coordinate = first; coordinate <= last; coordinate++)
                {
                    token.ThrowIfCancellationRequested();
                    var address = vertical ? new CellAddress(coordinate, property.Key == "left" ? range.First.Column : range.Last.Column) :
                        new CellAddress(property.Key == "top" ? range.First.Row : range.Last.Row, coordinate);
                    var cell = cells.GetValueOrDefault(address) ?? new ReportCell(string.Empty, blankStyle(address));
                    cells[address] = cell with { Style = styles.Intern(ConditionalStyle.Apply(cell.Style, edge)) };
                }
            }
        }

        Core.Rendering.ConversionMetrics.Report("conditionalFormatting.evaluations", evaluations);
        Core.Rendering.ConversionMetrics.Report("conditionalFormatting.candidateVisits", visits);
        Core.Rendering.ConversionMetrics.Report("conditionalFormatting.candidates", candidates.Count);
        Core.Rendering.ConversionMetrics.Report("conditionalFormatting.ruleNormalizationWork", ruleWork);
        Core.Rendering.ConversionMetrics.Report("conditionalFormatting.retainedRuleReferences", ruleReferences);
    }

    private static string Id(PreparedRule rule) => "conditional-format-" + (rule.Order + 1).ToString(CultureInfo.InvariantCulture);

    private static UnsupportedFeature Feature(PreparedRule rule, string code, string message) => new(code, message, Id(rule), rule.Source.Ranges.Select(range => new Model.CellRange(new(range.First.Row, range.First.Column), new(range.Last.Row, range.Last.Column))).ToArray());

    private static PreparedRule Prepare(ConditionalFormattingRuleInfo source, int order, FormulaEvaluationOptions? options)
    {
        var result = new PreparedRule(source, order, int.TryParse(Attribute(source.Source, "priority"), NumberStyles.None, CultureInfo.InvariantCulture, out var priority) ? priority : int.MaxValue);
        var type = Attribute(source.Source, "type");
        var visual = type is "dataBar" or "iconSet";
        var extensionSupported = visual && (source.Extension is not null || source.Source.NamespaceUri == ConditionalFormattingRuleNormalizer.X14) && !ConditionalFormattingRuleNormalizer.UnknownExtension(source.Source);
        if ((source.IsExtension && !extensionSupported) || source.Ranges.Count == 0 || type is not ("cellIs" or "expression" or "containsText" or "notContainsText" or "beginsWith" or "endsWith" or "containsBlanks" or "notContainsBlanks" or "containsErrors" or "notContainsErrors" or "duplicateValues" or "uniqueValues" or "top10" or "aboveAverage" or "colorScale" or "dataBar" or "iconSet"))
        {
            result.Failure = source.IsExtension ? "ConditionalFormattingExtensionUnsupported" : "UnsupportedConditionalFormatting";
            return result;
        }

        if (Attribute(source.Source, "dxfId") is not null && source.Style is null)
        {
            result.Failure = "ConditionalFormattingDifferentialInvalid";
            return result;
        }

        if (type is "duplicateValues" or "uniqueValues" or "top10" or "aboveAverage" or "colorScale" or "dataBar" or "iconSet")
        {
            try
            {
                result.PopulationRule = new PopulationRule(source, options);
            }
            catch (ConditionalFormattingException exception)
            {
                result.Failure = exception.Code;
            }
        }

        if (type is "cellIs" or "expression")
        {
            var formulas = source.Source.Children.Where(child => child.Name == "formula").Select(child => child.Text.TrimStart('=')).ToArray();
            var anchor = source.Ranges[0].First.ToString();
            var expression = type == "expression" && formulas.Length == 1 ? formulas[0] : CellComparison(anchor, Attribute(source.Source, "operator"), formulas);
            if (expression is null)
            {
                result.Failure = "ConditionalFormattingFormulaUnsupported";
            }
            else
            {
                var parsed = FormulaParser.Parse(expression, options);
                result.Formula = parsed.Formula;
                if (!parsed.Success)
                {
                    result.Failure = parsed.Diagnostics.Any(diagnostic => diagnostic.Code == "ResourceLimitExceeded") ? "ConditionalFormattingLimitExceeded" : "ConditionalFormattingFormulaUnsupported";
                }
            }
        }

        return result;
    }

    private static string? CellComparison(string cell, string? operation, string[] formulas)
    {
        if (operation is "between" or "notBetween")
        {
            return formulas.Length != 2 ? null : operation == "between"
                ? "AND(" + cell + ">=(" + formulas[0] + ")," + cell + "<=(" + formulas[1] + "))"
                : "OR(" + cell + "<(" + formulas[0] + ")," + cell + ">(" + formulas[1] + "))";
        }

        var comparison = operation switch
        {
            "equal" => "=",
            "notEqual" => "<>",
            "greaterThan" => ">",
            "greaterThanOrEqual" => ">=",
            "lessThan" => "<",
            "lessThanOrEqual" => "<=",
            _ => null,
        };
        return formulas.Length == 1 && comparison is not null ? cell + comparison + "(" + formulas[0] + ")" : null;
    }

    private void ReportCandidateLimit(PreparedRule rule, string message)
    {
        // Every limit rolls back the whole sheet, including rules not yet collected.
        targetSearch ??= new(info, options, isTarget, cancellationToken: cancellationToken);
        var result = targetSearch.Find(AffectedRanges(), cancellationToken);
        if (result.State == TargetSearchState.Indeterminate)
        {
            throw new InvalidOperationException("The bounded conditional-formatting target search could not prove whether omitted formatting affects the output.");
        }

        if (result.State == TargetSearchState.Found)
        {
            diagnostics[(rule.Order, "ConditionalFormattingLimitExceeded")] = new("ConditionalFormattingLimitExceeded", DiagnosticSeverity.Warning, DiagnosticStage.Read, message, info.Sheet.Name, result.Address.ToString(), ObjectId: Id(rule));
        }

        IEnumerable<SourceRange> AffectedRanges()
        {
            long work = 0;
            foreach (var range in rules.SelectMany(item => item.Source.Ranges))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var region in limitRegions)
                {
                    CheckWork();
                    var firstRow = Math.Max(range.First.Row, region.First.Row);
                    var firstColumn = Math.Max(range.First.Column, region.First.Column);
                    var lastRow = Math.Min(range.Last.Row, region.Last.Row);
                    var lastColumn = Math.Min(range.Last.Column, region.Last.Column);
                    if (firstRow <= lastRow && firstColumn <= lastColumn)
                    {
                        yield return new(new(firstRow, firstColumn), new(lastRow, lastColumn));
                    }
                }

                foreach (var merge in info.MergedRanges)
                {
                    CheckWork();
                    if (range.Contains(merge.First) && IntersectsOutput(merge))
                    {
                        yield return new(merge.First, merge.First);
                    }
                }
            }

            bool IntersectsOutput(SourceRange merge)
            {
                foreach (var region in limitRegions)
                {
                    CheckWork();
                    if (merge.First.Row <= region.Last.Row && merge.Last.Row >= region.First.Row && merge.First.Column <= region.Last.Column && merge.Last.Column >= region.First.Column)
                    {
                        return true;
                    }
                }

                return false;
            }

            void CheckWork()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++work > CandidateWorkLimit)
                {
                    throw new InvalidOperationException("The bounded affected-range search could not prove whether omitted formatting affects the output.");
                }
            }
        }
    }

    private bool Matches(PreparedRule rule, OpenXml.Reader.Model.Cells.CellAddress address)
    {
        var origin = new FormulaCellAddress(info.Sheet.Name, address.Row, address.Column);
        if (rule.Formula is { } formula)
        {
            var anchor = rule.Source.Ranges[0].First;
            var result = calculation!.EvaluateFormula(formula, new(info.Sheet.Name, anchor.Row, anchor.Column), origin);
            if (!result.Success)
            {
                throw new FormulaCalculationException(origin, result.Diagnostics);
            }

            // A legitimate Excel error is a nonmatching condition, not an engine failure.
            return result.Value.Kind == FormulaValueKind.Boolean ? result.Value.Boolean : result.Value.Kind == FormulaValueKind.Number && result.Value.Number != 0;
        }

        var value = calculation!.ReadValue(origin);
        var text = value.Kind switch
        {
            FormulaValueKind.Text => value.Text,
            FormulaValueKind.Number => value.Number.ToString("G15", CultureInfo.InvariantCulture),
            FormulaValueKind.Boolean => value.Boolean ? "TRUE" : "FALSE",
            _ => string.Empty,
        };
        var search = Attribute(rule.Source.Source, "text") ?? string.Empty;
        return Attribute(rule.Source.Source, "type") switch
        {
            "containsBlanks" => value.Kind == FormulaValueKind.Blank || (value.Kind == FormulaValueKind.Text && text.Trim(' ').Length == 0),
            "notContainsBlanks" => value.Kind != FormulaValueKind.Blank && (value.Kind != FormulaValueKind.Text || text.Trim(' ').Length != 0),
            "containsErrors" => value.Kind == FormulaValueKind.Error,
            "notContainsErrors" => value.Kind != FormulaValueKind.Error,
            "containsText" => value.Kind != FormulaValueKind.Error && text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0,
            "notContainsText" => value.Kind == FormulaValueKind.Error || text.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0,
            "beginsWith" => value.Kind != FormulaValueKind.Error && text.StartsWith(search, StringComparison.OrdinalIgnoreCase),
            "endsWith" => value.Kind != FormulaValueKind.Error && text.EndsWith(search, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private sealed class PreparedRule
    {
        internal PreparedRule(ConditionalFormattingRuleInfo source, int order, int priority, bool prepareStyle = true)
        {
            Source = source;
            Order = order;
            Priority = priority;
            Style = new(prepareStyle ? source.Style?.Source : null);
        }

        internal ConditionalFormattingRuleInfo Source { get; }

        internal int Order { get; }

        internal int Priority { get; }

        internal ConditionalStyle Style { get; }

        internal ParsedFormula? Formula { get; set; }

        internal string? Failure { get; set; }

        internal PopulationRule? PopulationRule { get; set; }
    }
}
