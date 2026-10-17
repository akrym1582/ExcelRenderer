using System;
using ExcelRenderer.Formulas.Functions;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;

namespace ExcelRenderer.Formulas.Evaluation
{
    /// <summary>Implements bounded session evaluation for this function family.</summary>
    public sealed partial class FormulaSession
    {
        private Item Function(FormulaNode node, FormulaCellAddress origin)
        {
            var name = node.Text.ToUpperInvariant();
            FunctionRegistry.TryGet(name, out var definition);
            switch (definition.Family)
            {
                case FunctionFamily.Numeric: return NumericFunction(name, node.Children, origin);
                case FunctionFamily.Aggregate: return new Item(Aggregate(name, node.Children, origin));
                case FunctionFamily.Logical: return LogicalFunction(name, node.Children, origin);
                case FunctionFamily.Criteria: return new Item(ConditionalAggregate(name, node, origin));
                case FunctionFamily.Lookup: return LookupFunction(name, node, origin);
                case FunctionFamily.Date: return new Item(DateFunction(name, node, origin));
                case FunctionFamily.Text: return new Item(TextFunction(name, node, origin));
                default: throw new InvalidOperationException("Unregistered function family.");
            }
        }
    }
}
