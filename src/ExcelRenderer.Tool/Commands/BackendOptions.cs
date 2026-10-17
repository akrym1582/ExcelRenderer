using System.CommandLine;
using ExcelRenderer.Excel;
using ExcelRenderer.Mapping;

namespace ExcelRenderer.Tool.Commands;

/// <summary>Shares backend parsing, validation and optional stderr decisions across commands.</summary>
internal sealed class BackendOptions
{
    private readonly Option<string>? calculationMode;
    private readonly bool recalculateMappedWorkbook;
    private readonly Option<string?> mapping = new("--mapping-backend") { Description = "Mapping engine: openxml or auto. Requires --data." };
    private readonly Option<string> reader = new("--reader-backend") { Description = "Reader engine: openxml or auto.", DefaultValueFactory = _ => "openxml" };
    private readonly Option<bool> verbose = new("--verbose") { Description = "Write backend selection and reasons to stderr." };

    /// <summary>Initializes a new instance of the <see cref="BackendOptions"/> class.</summary>
    /// <param name="command">The command receiving shared arguments.</param>
    /// <param name="data">The command's data argument.</param>
    /// <param name="formulaMode">The formula option; null when unavailable.</param>
    /// <param name="recalculateMappedWorkbook">Whether to calculate saved XLSX results before returning.</param>
    internal BackendOptions(Command command, Option<string?> data, Option<string>? formulaMode = null, bool recalculateMappedWorkbook = false)
    {
        calculationMode = formulaMode;
        this.recalculateMappedWorkbook = recalculateMappedWorkbook;
        command.Add(mapping);
        command.Add(verbose);
        if (formulaMode is not null && !recalculateMappedWorkbook)
        {
            command.Add(reader);
        }

        command.Validators.Add(result =>
        {
            if (result.GetValue(mapping) is { } mapped)
            {
                if (mapped is not ("openxml" or "auto"))
                {
                    result.AddError(mapped == "closedxml" ? "--mapping-backend: ClosedXML has been removed; use openxml or auto." : "--mapping-backend must be openxml or auto.");
                }

                if (result.GetValue(data) is null)
                {
                    result.AddError("--mapping-backend requires --data.");
                }
            }

            if (formulaMode is not null && !recalculateMappedWorkbook)
            {
                var selected = result.GetValue(reader) ?? "openxml";
                if (selected is not ("openxml" or "auto"))
                {
                    result.AddError(selected == "closedxml" ? "--reader-backend: ClosedXML has been removed; use openxml or auto." : "--reader-backend must be openxml or auto.");
                }
            }
        });
    }

    /// <summary>Builds read options with an optional selection observer.</summary>
    /// <param name="result">The validated parse result.</param>
    /// <param name="formulaMode">The formula argument.</param>
    /// <returns>Product read settings.</returns>
    internal ExcelReadOptions GetRead(ParseResult result, Option<string> formulaMode) => CommandSupport.GetReadOptions(result.GetValue(formulaMode)!) with
    {
        Backend = result.GetValue(reader) switch { "openxml" => ReaderBackend.OpenXml, "auto" => ReaderBackend.Auto, _ => ReaderBackend.OpenXml },
        BackendSelected = result.GetValue(verbose) ? selection => Console.Error.WriteLine($"Reader: {selection.SelectedBackend} ({string.Join(", ", selection.ReasonCodes)})") : null,
    };

    /// <summary>Builds mapping settings with an optional selection observer.</summary>
    /// <param name="result">The validated parse result.</param>
    /// <returns>Product mapping settings.</returns>
    internal MappingOptions GetMapping(ParseResult result) => new()
    {
        FormulaCalculationMode = calculationMode is not null && result.GetValue(calculationMode) == "recalculate" ? OpenXml.Calculation.FormulaCalculationMode.Recalculate : OpenXml.Calculation.FormulaCalculationMode.CachedValue,
        RecalculateMappedWorkbook = recalculateMappedWorkbook,
        Backend = result.GetValue(mapping) switch { "openxml" => MappingBackend.OpenXml, "auto" => MappingBackend.Auto, _ => MappingBackend.OpenXml },
        BackendSelected = result.GetValue(verbose) ? selection => Console.Error.WriteLine($"Mapping: {selection.SelectedBackend} ({string.Join(", ", selection.ReasonCodes)})") : null,
    };
}
