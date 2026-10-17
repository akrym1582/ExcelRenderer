using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;

namespace ExcelRenderer.Formulas
{
    /// <summary>Evaluates reachable dependencies with session-local memoization over an immutable data source. Instances are not thread safe.</summary>
    public sealed class FormulaSession
    {
        private readonly IFormulaDataSource source;
        private readonly FormulaEvaluationOptions options;
        private readonly CancellationToken cancellation;
        private readonly Dictionary<FormulaCellAddress, FormulaEvaluationResult> results = new Dictionary<FormulaCellAddress, FormulaEvaluationResult>();
        private readonly Dictionary<FormulaCellAddress, FormulaValue> values = new Dictionary<FormulaCellAddress, FormulaValue>();
        private readonly HashSet<FormulaCellAddress> visiting = new HashSet<FormulaCellAddress>();
        private readonly List<FormulaCellAddress> path = new List<FormulaCellAddress>();
        private int visits;
        private int steps;
        private int formulas;
        private long cachedStringCharacters;

        /// <summary>Initializes a new instance of the <see cref="FormulaSession"/> class.</summary>
        /// <param name="source">The source.</param>
        /// <param name="options">The options.</param>
        /// <param name="cancellation">The cancellation.</param>
        internal FormulaSession(IFormulaDataSource source, FormulaEvaluationOptions options, CancellationToken cancellation)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            if (options.MaxStringLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options.MaxStringLength));
            }

            if (options.MaxCachedStringCharacters < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options.MaxCachedStringCharacters));
            }

            this.options = new FormulaEvaluationOptions
            {
                MaxFormulaLength = options.MaxFormulaLength,
                MaxAstNodes = options.MaxAstNodes,
                MaxSyntaxDepth = options.MaxSyntaxDepth,
                MaxDependencyDepth = options.MaxDependencyDepth,
                MaxFormulaCells = options.MaxFormulaCells,
                MaxCellVisits = options.MaxCellVisits,
                MaxEvaluationSteps = options.MaxEvaluationSteps,
                MaxStringLength = options.MaxStringLength,
                MaxCachedStringCharacters = options.MaxCachedStringCharacters,
            };
            this.cancellation = cancellation;
            EvaluatedCells = new ReadOnlyDictionary<FormulaCellAddress, FormulaEvaluationResult>(results);
        }

        /// <summary>Gets the formula results and diagnostics cached in this session.</summary>
        public IReadOnlyDictionary<FormulaCellAddress, FormulaEvaluationResult> EvaluatedCells { get; }

        /// <summary>Evaluates a cell and its reachable dependencies.</summary>
        /// <param name="target">The target.</param>
        /// <returns>The resulting value.</returns>
        public FormulaEvaluationResult EvaluateCell(FormulaCellAddress target)
        {
            try
            {
                var value = Read(target);
                return new FormulaEvaluationResult(value.Kind == FormulaValueKind.Blank ? FormulaValue.FromNumber(0) : value);
            }
            catch (Failure failure)
            {
                return new FormulaEvaluationResult(failure.Diagnostic);
            }
        }

        /// <summary>Evaluates a parsed formula at the supplied origin.</summary>
        /// <param name="formula">The formula.</param>
        /// <param name="origin">The origin.</param>
        /// <returns>The resulting value.</returns>
        public FormulaEvaluationResult Evaluate(ParsedFormula formula, FormulaCellAddress origin)
        {
            if (formula == null)
            {
                throw new ArgumentNullException(nameof(formula));
            }

            try
            {
                CheckParsedLimits(formula, origin);
                Validate(formula.Root, origin);
                var value = Scalar(Eval(formula.Root, origin), origin);
                return new FormulaEvaluationResult(value.Kind == FormulaValueKind.Blank ? FormulaValue.FromNumber(0) : value);
            }
            catch (Failure failure)
            {
                return new FormulaEvaluationResult(failure.Diagnostic);
            }
        }

        private static FormulaValue Numeric(FormulaValue value)
        {
            switch (value.Kind)
            {
                case FormulaValueKind.Blank: return Number(0);
                case FormulaValueKind.Boolean: return Number(value.Boolean ? 1 : 0);
                case FormulaValueKind.Text:
                    var text = value.Text.Trim(' ', '\t', '\r', '\n');
                    return double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var number) && !double.IsNaN(number) && !double.IsInfinity(number) ? Number(number) : FormulaValue.FromError("#VALUE!");
                default: return value;
            }
        }

        private static FormulaValue Logical(FormulaValue value)
        {
            if (value.Kind == FormulaValueKind.Error || value.Kind == FormulaValueKind.Boolean)
            {
                return value;
            }

            if (value.Kind == FormulaValueKind.Blank)
            {
                return FormulaValue.FromBoolean(false);
            }

            if (value.Kind == FormulaValueKind.Number)
            {
                return FormulaValue.FromBoolean(value.Number != 0);
            }

            return bool.TryParse(value.Text, out var boolean) ? FormulaValue.FromBoolean(boolean) : FormulaValue.FromError("#VALUE!");
        }

        private static FormulaValue Number(double number) => double.IsNaN(number) || double.IsInfinity(number) ? FormulaValue.FromError("#NUM!") : FormulaValue.FromNumber(number);

        private static int Compare(FormulaValue left, FormulaValue right)
        {
            if (left.Kind == FormulaValueKind.Blank)
            {
                left = Zero(right.Kind);
            }

            if (right.Kind == FormulaValueKind.Blank)
            {
                right = Zero(left.Kind);
            }

            if (left.Kind != right.Kind)
            {
                return left.Kind.CompareTo(right.Kind);
            }

            return left.Kind == FormulaValueKind.Number ? left.Number.CompareTo(right.Number) : left.Kind == FormulaValueKind.Boolean ? left.Boolean.CompareTo(right.Boolean) : StringComparer.OrdinalIgnoreCase.Compare(left.Text, right.Text);
        }

        private static FormulaValue Zero(FormulaValueKind kind) => kind == FormulaValueKind.Text ? FormulaValue.FromText(string.Empty) : kind == FormulaValueKind.Boolean ? FormulaValue.FromBoolean(false) : Number(0);

        private void CheckStringLength(long length, FormulaCellAddress origin, SourceSpan span)
        {
            if (length > options.MaxStringLength)
            {
                Fail("ResourceLimitExceeded", "MaxStringLength limit exceeded.", origin, span, nameof(options.MaxStringLength));
            }
        }

        private void Cache(FormulaCellAddress address, FormulaValue value, FormulaCellAddress origin, SourceSpan span)
        {
            if (value.Kind == FormulaValueKind.Text)
            {
                CheckStringLength(value.Text.Length, origin, span);
                if (value.Text.Length > options.MaxCachedStringCharacters - cachedStringCharacters)
                {
                    Fail("ResourceLimitExceeded", "MaxCachedStringCharacters limit exceeded.", origin, span, nameof(options.MaxCachedStringCharacters));
                }

                cachedStringCharacters += value.Text.Length;
            }

            values.Add(address, value);
        }

        private void Tick(FormulaCellAddress origin, SourceSpan span = default)
        {
            cancellation.ThrowIfCancellationRequested();
            if (++steps > options.MaxEvaluationSteps)
            {
                Fail("ResourceLimitExceeded", "Evaluation step limit exceeded.", origin, span);
            }
        }

        private void Fail(string code, string message, FormulaCellAddress origin, SourceSpan span = default, string? feature = null) => throw new Failure(new FormulaDiagnostic(code, message, span, origin, feature, path.ToArray()));

        private FormulaValue Read(FormulaCellAddress address, FormulaCellAddress? origin = null, SourceSpan span = default)
        {
            Tick(address);
            if (++visits > options.MaxCellVisits)
            {
                Fail("ResourceLimitExceeded", "Cell visit limit exceeded.", address);
            }

            if (values.TryGetValue(address, out var cached))
            {
                return cached;
            }

            if (results.TryGetValue(address, out var prior) && !prior.Success)
            {
                throw new Failure(prior.Diagnostics[0]);
            }

            if (visiting.Contains(address))
            {
                var cycle = new List<FormulaCellAddress>(path) { address };
                throw new Failure(new FormulaDiagnostic("CircularReference", "Circular cell dependency.", cell: address, dependencyPath: cycle.AsReadOnly()));
            }

            FormulaCellData cell;
            try
            {
                cell = source.GetCell(address);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Fail("DataSourceFailure", ex.Message, address);
                return default;
            }

            if (cell.Formula == null)
            {
                Cache(address, cell.Value, origin ?? address, span);
                return cell.Value;
            }

            if (++formulas > options.MaxFormulaCells || path.Count >= options.MaxDependencyDepth)
            {
                Fail("ResourceLimitExceeded", "Formula cell or dependency depth limit exceeded.", address);
            }

            visiting.Add(address);
            path.Add(address);
            try
            {
                var parsed = FormulaParser.Parse(cell.Formula, options);
                if (!parsed.Success)
                {
                    var d = parsed.Diagnostics[0];
                    Fail(d.Code, d.Message, address, d.Span, d.Feature);
                }

                Validate(parsed.Formula!.Root, address);
                var value = Scalar(Eval(parsed.Formula.Root, address), address);
                if (value.Kind == FormulaValueKind.Blank)
                {
                    value = Number(0);
                }

                Cache(address, value, address, parsed.Formula.Root.Span);
                results[address] = new FormulaEvaluationResult(value.Kind == FormulaValueKind.Blank ? FormulaValue.FromNumber(0) : value);
                return value;
            }
            catch (Failure failure)
            {
                results[address] = new FormulaEvaluationResult(failure.Diagnostic);
                throw;
            }
            finally
            {
                visiting.Remove(address);
                path.RemoveAt(path.Count - 1);
            }
        }

        private void CheckParsedLimits(ParsedFormula formula, FormulaCellAddress origin)
        {
            if (formula.Text.Length > options.MaxFormulaLength)
            {
                Fail("ResourceLimitExceeded", "Formula length limit exceeded.", origin);
            }

            var pending = new Stack<(FormulaNode Node, int Depth)>();
            pending.Push((formula.Root, 1));
            var count = 0;
            while (pending.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var current = pending.Pop();
                if (++count > options.MaxAstNodes || current.Depth > options.MaxSyntaxDepth)
                {
                    Fail("ResourceLimitExceeded", "Parsed AST node or depth limit exceeded.", origin, current.Node.Span);
                }

                foreach (var child in current.Node.Children)
                {
                    pending.Push((child, current.Depth + 1));
                }
            }
        }

        private void Validate(FormulaNode node, FormulaCellAddress origin)
        {
            Tick(origin, node.Span);
            if (node.Kind == FormulaNodeKind.Function)
            {
                var name = node.Text.ToUpperInvariant();
                int min;
                int max;
                switch (name)
                {
                    case "SUM": case "COUNT": case "COUNTA": min = 0; max = 255; break;
                    case "MIN": case "MAX": case "AVERAGE": case "AND": case "OR": min = 1; max = 255; break;
                    case "IF": min = 2; max = 3; break;
                    case "IFERROR": case "MOD": case "ROUND": case "ROUNDUP": case "ROUNDDOWN": min = max = 2; break;
                    case "NOT": case "ABS": case "INT": min = max = 1; break;
                    case "TRUE": case "FALSE": min = max = 0; break;
                    default: Fail("UnsupportedFunction", "Unsupported function: " + node.Text, origin, node.Span, node.Text); return;
                }

                if (node.Children.Count < min || node.Children.Count > max)
                {
                    Fail("InvalidArgumentCount", "Invalid argument count for " + name, origin, node.Span, name);
                }
            }

            foreach (var child in node.Children)
            {
                Validate(child, origin);
            }
        }

        private Item Eval(FormulaNode node, FormulaCellAddress origin)
        {
            Tick(origin, node.Span);
            switch (node.Kind)
            {
                case FormulaNodeKind.Number: return new Item(Number(node.Number));
                case FormulaNodeKind.Text:
                    CheckStringLength(node.Text.Length, origin, node.Span);
                    return new Item(FormulaValue.FromText(node.Text));
                case FormulaNodeKind.Boolean: return new Item(FormulaValue.FromBoolean(node.Boolean));
                case FormulaNodeKind.Error: return new Item(FormulaValue.FromError(node.Text));
                case FormulaNodeKind.Missing: return new Item(Number(0));
                case FormulaNodeKind.Parenthesized: return Eval(node.Children[0], origin);
                case FormulaNodeKind.Cell: case FormulaNodeKind.Range: return Reference(node, origin);
                case FormulaNodeKind.Name:
                    FormulaRange named;
                    try
                    {
                        if (!source.TryResolveName(node.Sheet, node.Text, origin.SheetId, out named))
                        {
                            return new Item(FormulaValue.FromError("#NAME?"));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Fail("DataSourceFailure", ex.Message, origin, node.Span);
                        return default;
                    }

                    return new Item(named, node.Span);
                case FormulaNodeKind.Unary:
                case FormulaNodeKind.Percent:
                    var unary = Numeric(Scalar(Eval(node.Children[0], origin), origin));
                    if (unary.Kind == FormulaValueKind.Error)
                    {
                        return new Item(unary);
                    }

                    return new Item(Number(node.Kind == FormulaNodeKind.Percent ? unary.Number / 100 : node.Text == "-" ? -unary.Number : unary.Number));
                case FormulaNodeKind.Binary: return new Item(Binary(node, origin));
                case FormulaNodeKind.Function: return Function(node, origin);
                default: Fail("UnsupportedSyntax", "Unsupported node.", origin, node.Span); return default;
            }
        }

        private Item Reference(FormulaNode node, FormulaCellAddress origin)
        {
            var first = node.Kind == FormulaNodeKind.Range ? node.Children[0] : node;
            var last = node.Kind == FormulaNodeKind.Range ? node.Children[1] : node;
            var sheet = node.Sheet ?? first.Sheet;
            string id = origin.SheetId;
            if (sheet != null)
            {
                try
                {
                    if (!source.TryResolveSheet(sheet, out id))
                    {
                        return new Item(FormulaValue.FromError("#REF!"));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Fail("DataSourceFailure", ex.Message, origin, node.Span);
                }
            }

            return new Item(new FormulaRange(id, first.Row == 0 ? 1 : first.Row, first.Column == 0 ? 1 : first.Column, last.Row == 0 ? 1048576 : last.Row, last.Column == 0 ? 16384 : last.Column), node.Span);
        }

        private FormulaValue Scalar(Item item, FormulaCellAddress origin)
        {
            if (!item.IsReference)
            {
                return item.Value;
            }

            var r = item.Range;
            if (r.FirstRow != r.LastRow || r.FirstColumn != r.LastColumn)
            {
                Fail("UnsupportedArrayEvaluation", "A multi-cell range cannot be returned as a scalar.", origin);
            }

            return Read(new FormulaCellAddress(r.SheetId, r.FirstRow, r.FirstColumn), origin, item.Span);
        }

        private IEnumerable<FormulaValue> Enumerate(Item item, FormulaCellAddress origin)
        {
            if (!item.IsReference)
            {
                yield return item.Value;
                yield break;
            }

            var r = item.Range;
            for (var row = r.FirstRow; row <= r.LastRow; row++)
            {
                for (var col = r.FirstColumn; col <= r.LastColumn; col++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    yield return Read(new FormulaCellAddress(r.SheetId, row, col), origin, item.Span);
                }
            }
        }

        private FormulaValue Binary(FormulaNode node, FormulaCellAddress origin)
        {
            var left = Scalar(Eval(node.Children[0], origin), origin);
            bool arithmetic = node.Text == "+" || node.Text == "-" || node.Text == "*" || node.Text == "/" || node.Text == "^";
            if (arithmetic)
            {
                left = Numeric(left);
            }

            if (left.Kind == FormulaValueKind.Error)
            {
                return left;
            }

            var right = Scalar(Eval(node.Children[1], origin), origin);
            if (right.Kind == FormulaValueKind.Error)
            {
                return right;
            }

            if (node.Text == "&")
            {
                string leftText = left.ToString();
                string rightText = right.ToString();
                var operatorSpan = new SourceSpan(node.Children[0].Span.Start + node.Children[0].Span.Length, node.Children[1].Span.Start - node.Children[0].Span.Start - node.Children[0].Span.Length);
                CheckStringLength((long)leftText.Length + rightText.Length, origin, operatorSpan);
                return FormulaValue.FromText(string.Concat(leftText, rightText));
            }

            if (node.Text == "=" || node.Text == "<>" || node.Text == "<" || node.Text == "<=" || node.Text == ">" || node.Text == ">=")
            {
                var compare = Compare(left, right);
                return FormulaValue.FromBoolean(node.Text == "=" ? compare == 0 : node.Text == "<>" ? compare != 0 : node.Text == "<" ? compare < 0 : node.Text == "<=" ? compare <= 0 : node.Text == ">" ? compare > 0 : compare >= 0);
            }

            left = Numeric(left);
            right = Numeric(right);
            if (left.Kind == FormulaValueKind.Error)
            {
                return left;
            }

            if (right.Kind == FormulaValueKind.Error)
            {
                return right;
            }

            var a = left.Number;
            var b = right.Number;
            switch (node.Text)
            {
                case "+": return Number(a + b);
                case "-": return Number(a - b);
                case "*": return Number(a * b);
                case "/": return b == 0 ? FormulaValue.FromError("#DIV/0!") : Number(a / b);
                case "^": return a == 0 && b == 0 ? FormulaValue.FromError("#NUM!") : a == 0 && b < 0 ? FormulaValue.FromError("#DIV/0!") : Number(Math.Pow(a, b));
                default: return FormulaValue.FromError("#VALUE!");
            }
        }

        private Item Function(FormulaNode node, FormulaCellAddress origin)
        {
            var name = node.Text.ToUpperInvariant();
            var args = node.Children;
            if (name == "TRUE" || name == "FALSE")
            {
                return new Item(FormulaValue.FromBoolean(name == "TRUE"));
            }

            if (name == "IF")
            {
                var condition = Logical(Scalar(Eval(args[0], origin), origin));
                if (condition.Kind == FormulaValueKind.Error)
                {
                    return new Item(condition);
                }

                return condition.Boolean ? Eval(args[1], origin) : args.Count == 3 ? Eval(args[2], origin) : new Item(FormulaValue.FromBoolean(false));
            }

            if (name == "IFERROR")
            {
                var first = Eval(args[0], origin);
                if (first.IsReference && (first.Range.FirstRow != first.Range.LastRow || first.Range.FirstColumn != first.Range.LastColumn))
                {
                    Fail("UnsupportedArrayEvaluation", "IFERROR requires a scalar first argument.", origin, args[0].Span, "IFERROR");
                }

                return Scalar(first, origin).Kind == FormulaValueKind.Error ? Eval(args[1], origin) : first;
            }

            if (name == "SUM" || name == "COUNT" || name == "COUNTA" || name == "MIN" || name == "MAX" || name == "AVERAGE" || name == "AND" || name == "OR")
            {
                return new Item(Aggregate(name, args, origin));
            }

            var value = Scalar(Eval(args[0], origin), origin);
            if (name == "NOT")
            {
                value = Logical(value);
                return new Item(value.Kind == FormulaValueKind.Error ? value : FormulaValue.FromBoolean(!value.Boolean));
            }

            value = Numeric(value);
            if (value.Kind == FormulaValueKind.Error)
            {
                return new Item(value);
            }

            if (name == "ABS")
            {
                return new Item(Number(Math.Abs(value.Number)));
            }

            if (name == "INT")
            {
                return new Item(Number(Math.Floor(value.Number)));
            }

            var second = Numeric(Scalar(Eval(args[1], origin), origin));
            if (second.Kind == FormulaValueKind.Error)
            {
                return new Item(second);
            }

            if (name == "MOD")
            {
                return new Item(second.Number == 0 ? FormulaValue.FromError("#DIV/0!") : Number(value.Number - (second.Number * Math.Floor(value.Number / second.Number))));
            }

            return new Item(Number(DecimalRounding.Round(value.Number, second.Number, name)));
        }

        private FormulaValue Aggregate(string name, IReadOnlyList<FormulaNode> args, FormulaCellAddress origin)
        {
            double sum = 0;
            double extreme = 0;
            int count = 0;
            bool logical = name == "AND";
            FormulaValue? error = null;
            foreach (var arg in args)
            {
                var item = Eval(arg, origin);
                foreach (var raw in Enumerate(item, origin))
                {
                    Tick(origin, arg.Span);
                    if (name == "COUNTA")
                    {
                        if (raw.Kind != FormulaValueKind.Blank)
                        {
                            count++;
                        }

                        continue;
                    }

                    if (name == "COUNT")
                    {
                        if (raw.Kind == FormulaValueKind.Number || (!item.IsReference && Numeric(raw).Kind == FormulaValueKind.Number && raw.Kind != FormulaValueKind.Blank))
                        {
                            count++;
                        }

                        continue;
                    }

                    if (raw.Kind == FormulaValueKind.Error)
                    {
                        error ??= raw;
                        continue;
                    }

                    if (name == "AND" || name == "OR")
                    {
                        if (item.IsReference && (raw.Kind == FormulaValueKind.Text || raw.Kind == FormulaValueKind.Blank))
                        {
                            continue;
                        }

                        var boolean = Logical(raw);
                        if (boolean.Kind == FormulaValueKind.Error)
                        {
                            error ??= boolean;
                            continue;
                        }

                        logical = name == "AND" ? logical && boolean.Boolean : logical || boolean.Boolean;
                        count++;
                        continue;
                    }

                    if (item.IsReference && raw.Kind != FormulaValueKind.Number)
                    {
                        continue;
                    }

                    var value = Numeric(raw);
                    if (value.Kind == FormulaValueKind.Error)
                    {
                        error ??= value;
                        continue;
                    }

                    sum += value.Number;
                    if (count == 0 || (name == "MIN" && value.Number < extreme) || (name == "MAX" && value.Number > extreme))
                    {
                        extreme = value.Number;
                    }

                    count++;
                }
            }

            if (error.HasValue)
            {
                return error.Value;
            }

            if (name == "COUNT" || name == "COUNTA")
            {
                return Number(count);
            }

            if (name == "AND" || name == "OR")
            {
                return count == 0 ? FormulaValue.FromError("#VALUE!") : FormulaValue.FromBoolean(logical);
            }

            if (name == "AVERAGE")
            {
                return count == 0 ? FormulaValue.FromError("#DIV/0!") : Number(sum / count);
            }

            return Number(name == "MIN" || name == "MAX" ? extreme : sum);
        }

        private readonly struct Item
        {
            /// <summary>Initializes a new instance of the <see cref="Item"/> struct.</summary>
            /// <param name="value">The value.</param>
            /// <returns>The resulting value.</returns>
            public Item(FormulaValue value)
            {
                Value = value;
                Range = default;
                IsReference = false;
                Span = default;
            }

            /// <summary>Initializes a new instance of the <see cref="Item"/> struct.</summary>
            /// <param name="range">The range.</param>
            /// <param name="span">The reference source span.</param>
            /// <returns>The resulting value.</returns>
            public Item(FormulaRange range, SourceSpan span)
            {
                Range = range;
                Value = default;
                IsReference = true;
                Span = span;
            }

            /// <summary>Gets value.</summary>
            public FormulaValue Value { get; }

            /// <summary>Gets range.</summary>
            public FormulaRange Range { get; }

            /// <summary>Gets a value indicating whether this is a reference.</summary>
            public bool IsReference { get; }

            /// <summary>Gets the reference source span.</summary>
            public SourceSpan Span { get; }
        }

        private sealed class Failure : Exception
        {
            /// <summary>Initializes a new instance of the <see cref="Failure"/> class.</summary>
            /// <param name="diagnostic">The diagnostic.</param>
            /// <returns>The resulting value.</returns>
            public Failure(FormulaDiagnostic diagnostic)
            {
                Diagnostic = diagnostic;
            }

            /// <summary>Gets diagnostic.</summary>
            public FormulaDiagnostic Diagnostic { get; }
        }
    }
}
