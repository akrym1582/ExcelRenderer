using System.Threading;

namespace ExcelRenderer.Formulas
{
    /// <summary>Creates isolated formula evaluation sessions.</summary>
    public sealed class FormulaEngine
    {
        /// <summary>Creates a session with shared dependency results and a snapshot of the supplied limits.</summary>
        /// <param name="dataSource">The data source.</param>
        /// <param name="options">The options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The resulting value.</returns>
        public FormulaSession CreateSession(IFormulaDataSource dataSource, FormulaEvaluationOptions? options = null, CancellationToken cancellationToken = default) => new FormulaSession(dataSource, options ?? new FormulaEvaluationOptions(), cancellationToken);

        /// <summary>Evaluates a parsed formula at the supplied origin.</summary>
        /// <param name="formula">The formula.</param>
        /// <param name="dataSource">The data source.</param>
        /// <param name="origin">The origin.</param>
        /// <param name="options">The options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The resulting value.</returns>
        public FormulaEvaluationResult Evaluate(ParsedFormula formula, IFormulaDataSource dataSource, FormulaCellAddress origin, FormulaEvaluationOptions? options = null, CancellationToken cancellationToken = default) => CreateSession(dataSource, options, cancellationToken).Evaluate(formula, origin);

        /// <summary>Evaluates a cell and its reachable dependencies.</summary>
        /// <param name="target">The target.</param>
        /// <param name="dataSource">The data source.</param>
        /// <param name="options">The options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The resulting value.</returns>
        public FormulaEvaluationResult EvaluateCell(FormulaCellAddress target, IFormulaDataSource dataSource, FormulaEvaluationOptions? options = null, CancellationToken cancellationToken = default) => CreateSession(dataSource, options, cancellationToken).EvaluateCell(target);
    }
}
