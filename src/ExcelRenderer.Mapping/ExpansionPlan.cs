using System;
using System.Collections.Generic;
using System.Threading;

namespace ExcelRenderer.Mapping;

/// <summary>Evaluates the template language once, independently of output storage.</summary>
internal sealed class ExpansionPlan
{
    private readonly TemplateSheet template;
    private readonly object? root;
    private readonly MappingOptions options;
    private readonly CancellationToken cancellationToken;
    private int outputRows;

    /// <summary>Initializes a new instance of the <see cref="ExpansionPlan"/> class.</summary>
    /// <param name="template">Validated source syntax.</param>
    /// <param name="root">Caller input data.</param>
    /// <param name="options">Formatting and output limits.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    internal ExpansionPlan(TemplateSheet template, object? root, MappingOptions options, CancellationToken cancellationToken)
    {
        this.template = template;
        this.root = root;
        this.options = options;
        this.cancellationToken = cancellationToken;
        Nodes = Plan(template.Nodes, new Dictionary<string, object?>(StringComparer.Ordinal));
    }

    /// <summary>Gets evaluated output occurrences.</summary>
    internal List<ExpansionNode> Nodes { get; }

    private List<ExpansionNode> Plan(List<TemplateNode> templates, Dictionary<string, object?> aliases)
    {
        var result = new List<ExpansionNode>();
        foreach (var node in templates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var planned = new ExpansionNode(node);
            var items = node.Path is null ? new object?[] { null } :
                TemplateSheet.At(template.Sheet, node.Location, () => node.Path.Array(root, aliases, options.MaxOutputRows));
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node.IsBlock)
                {
                    var nestedAliases = new Dictionary<string, object?>(aliases, StringComparer.Ordinal) { [node.Alias!] = item };
                    planned.Blocks.Add(Plan(node.Children!, nestedAliases));
                }
                else
                {
                    outputRows = checked(outputRows + 1);
                    if (outputRows > options.MaxOutputRows)
                    {
                        throw new MappingException(template.Sheet.Name, node.Location.Address, node.Location.Text, "Expansion exceeds MaxOutputRows.");
                    }

                    var values = new List<MappedValue>();
                    foreach (var cell in node.Cells)
                    {
                        values.Add(TemplateSheet.At(template.Sheet, cell, () => Value(cell, aliases, item, node.Path is not null)));
                    }

                    planned.Rows.Add(values);
                }
            }

            result.Add(planned);
        }

        return result;
    }

    private MappedValue Value(TemplateCell cell, IReadOnlyDictionary<string, object?> aliases, object? item, bool implicitArray)
    {
        if (cell.Expression is null)
        {
            return cell.Text == "**@page-break" ? new MappedValue(null) : new MappedValue(cell.Text.Substring(1));
        }

        var value = cell.Expression.Evaluate(root, aliases, options, item, implicitArray);
        if (value is null)
        {
            return new MappedValue(null);
        }

        if (value is not string && value is not bool && value is not DateTime && value is not TimeSpan &&
            value is not byte && value is not sbyte && value is not short && value is not ushort &&
            value is not int && value is not uint && value is not long && value is not ulong &&
            value is not float && value is not double && value is not decimal)
        {
            throw new InvalidOperationException("A cell requires a scalar Excel value; use format() for other IFormattable types.");
        }

        if (value is string text && text.Length > 32767)
        {
            throw new InvalidOperationException("Excel cell strings cannot exceed 32767 characters.");
        }

        if ((value is double number && (double.IsNaN(number) || double.IsInfinity(number))) ||
            (value is float single && (float.IsNaN(single) || float.IsInfinity(single))))
        {
            throw new InvalidOperationException("Excel numeric values must be finite.");
        }

        return new MappedValue(value);
    }
}
