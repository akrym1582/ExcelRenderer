using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using ExcelRenderer.Mapping.Configuration;
using ExcelRenderer.Mapping.Diagnostics;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.OpenXml.Input;
using ExcelRenderer.OpenXml.Writing;

namespace ExcelRenderer.Mapping;

/// <summary>Maps CLR objects or JSON into XLSX templates independently of rendering.</summary>
public static partial class ExcelTemplateMapper
{
    /// <summary>Maps a template stream into an output stream, leaving both streams open.</summary>
    /// <param name="template">The readable XLSX template.</param>
    /// <param name="output">The writable XLSX destination.</param>
    /// <param name="data">The root CLR object or JSON element.</param>
    /// <param name="options">Optional formatting and expansion settings.</param>
    /// <param name="cancellationToken">Cancels parsing and expansion.</param>
    public static void Map(Stream template, Stream output, object? data, MappingOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (template is null || output is null)
        {
            throw new ArgumentNullException(template is null ? nameof(template) : nameof(output));
        }

        if (ReferenceEquals(template, output))
        {
            throw new ArgumentException("Template and output streams must differ.");
        }

        options ??= new MappingOptions();
        if (options.MaxOutputRows < 1 || options.MaxOutputRows > 1_048_576 || options.Culture is null || options.Input is null)
        {
            throw new ArgumentException("Specify a culture and MaxOutputRows between 1 and 1048576.", nameof(options));
        }

        if (!Enum.IsDefined(typeof(FormulaCalculationMode), options.FormulaCalculationMode))
        {
            throw new ArgumentOutOfRangeException(nameof(options.FormulaCalculationMode));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var prepared = WorkbookInputPreparer.ReadAsync(template, options.Input, cancellationToken).GetAwaiter().GetResult();
        using (var formulaSource = prepared.OpenRead())
        {
            var formula = WorkbookFormulaInspection.Find(formulaSource, cancellationToken);
            if (formula is not null && options.FormulaCalculationMode == FormulaCalculationMode.CachedValue)
            {
                throw new MappingException(
                    formula.Sheet,
                    formula.Cell,
                    formula.Name,
                    "MappingFormulaNotSupported: " + formula.Kind + ". Calculate values in the caller and replace template formulas with value placeholders.")
                {
                    ReasonCode = "MappingFormulaNotSupported",
                };
            }
        }

        using (var inspected = prepared.OpenRead())
        {
            var reasons = WorkbookWriteCapabilities.Inspect(inspected, cancellationToken, options.FormulaCalculationMode == FormulaCalculationMode.Recalculate);
            if (reasons.Count != 0)
            {
                throw new NotSupportedException("OpenXml mapping is unsupported: " + string.Join(", ", reasons));
            }
        }

        MapOpenXml(prepared, output, data, options, cancellationToken);
    }

    /// <summary>Maps JSON text into an XLSX template, leaving streams open.</summary>
    /// <param name="template">The readable XLSX template.</param>
    /// <param name="output">The writable XLSX destination.</param>
    /// <param name="json">The root JSON document.</param>
    /// <param name="options">Optional formatting and expansion settings.</param>
    /// <param name="cancellationToken">Cancels parsing and expansion.</param>
    public static void MapJson(Stream template, Stream output, string json, MappingOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(json);
        Map(template, output, document.RootElement, options, cancellationToken);
    }

    /// <summary>Maps files, validating and generating the workbook before opening the destination.</summary>
    /// <param name="templatePath">The template XLSX path.</param>
    /// <param name="outputPath">The destination XLSX path.</param>
    /// <param name="data">The root CLR object or JSON element.</param>
    /// <param name="options">Optional formatting and expansion settings.</param>
    /// <param name="cancellationToken">Cancels parsing and expansion.</param>
    public static void Map(string templatePath, string outputPath, object? data, MappingOptions? options = null, CancellationToken cancellationToken = default)
    {
        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.GetFullPath(templatePath), Path.GetFullPath(outputPath), comparison))
        {
            throw new ArgumentException("Template and output paths must differ.");
        }

        using var template = File.OpenRead(templatePath);
        var path = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".excelrenderer-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var result = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                Map(template, result, data, options, cancellationToken);
                result.Flush();
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
