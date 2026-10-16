using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.OpenXml;
using ExcelRenderer.OpenXml.Input;
using S = DocumentFormat.OpenXml.Spreadsheet;

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
        if (options.MaxOutputRows < 1 || options.MaxOutputRows > 1_048_576 || options.Culture is null || options.Input is null || !Enum.IsDefined(typeof(MappingBackend), options.Backend))
        {
            throw new ArgumentException("Specify a culture and MaxOutputRows between 1 and 1048576.", nameof(options));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var prepared = WorkbookInputPreparer.ReadAsync(template, options.Input, cancellationToken).GetAwaiter().GetResult();
        if (options.Backend != MappingBackend.ClosedXml)
        {
            using var inspected = prepared.OpenRead();
            var reasons = WorkbookWriteCapabilities.Inspect(inspected, cancellationToken);
            if (reasons.Count == 0)
            {
                if (MapOpenXml(prepared, output, data, options, cancellationToken))
                {
                    return;
                }
            }
            else if (options.Backend == MappingBackend.OpenXml)
            {
                throw new NotSupportedException("OpenXml mapping is unsupported: " + string.Join(", ", reasons));
            }
            else
            {
                options.BackendSelected?.Invoke(new MappingBackendSelection(options.Backend, MappingBackend.ClosedXml, reasons));
            }
        }
        else
        {
            options.BackendSelected?.Invoke(new MappingBackendSelection(options.Backend, MappingBackend.ClosedXml, Array.AsReadOnly(new[] { "ExplicitClosedXml" })));
        }

        using var source = prepared.OpenRead();
        OperationMetrics.Report("closedXml.workbooks", 1);
        using var workbook = new XLWorkbook(source);
        var templates = workbook.Worksheets.Select(sheet => new TemplateSheet(ClosedXmlTemplateAdapter.Create(sheet))).ToArray();
        foreach (var sheet in templates)
        {
            sheet.ValidateRepeatedRanges();
        }

        var plans = new List<WorksheetExpansion>();
        foreach (var sheet in templates)
        {
            var expansion = new WorksheetExpansion(sheet, workbook.Worksheet(sheet.Sheet.Name), new ExpansionPlan(sheet, data, options, cancellationToken), cancellationToken);
            plans.Add(expansion);
        }

        foreach (var expansion in plans)
        {
            expansion.Apply();
        }

        workbook.CalculateMode = XLCalculateMode.Auto;
        workbook.FullCalculationOnLoad = true;
        workbook.ForceFullCalculation = true;
        cancellationToken.ThrowIfCancellationRequested();

        // ClosedXML evaluates supported formulas and writes error values for unsupported functions.
        // The original formulas are retained for recalculation by Excel.
        using var result = new SpillableBufferStream(new InputBufferOptions
        {
            MemoryThresholdBytes = options.Input.MemoryThresholdBytes,
            AllowTemporaryFiles = options.Input.AllowTemporaryFiles,
            TemporaryDirectory = options.Input.TemporaryDirectory,
        });
        workbook.SaveAs(result, new SaveOptions { EvaluateFormulasBeforeSaving = true });
        NormalizePageBreakMode(result, plans);
        cancellationToken.ThrowIfCancellationRequested();
        result.Position = 0;
        result.CopyTo(output);
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

    private static void NormalizePageBreakMode(Stream result, List<WorksheetExpansion> plans)
    {
        var names = new HashSet<string>(plans.Where(p => p.HasExplicitPageBreak).Select(p => p.SheetName), StringComparer.Ordinal);
        if (names.Count == 0)
        {
            return;
        }

        result.Position = 0;
        using var document = SpreadsheetDocument.Open(result, true);
        var workbook = document.WorkbookPart!;
        foreach (var sheet in workbook.Workbook.Sheets!.Elements<S.Sheet>())
        {
            if (!names.Contains(sheet.Name!.Value!))
            {
                continue;
            }

            var worksheet = ((WorksheetPart)workbook.GetPartById(sheet.Id!.Value!)).Worksheet;
            worksheet.SheetProperties ??= new S.SheetProperties();
            worksheet.SheetProperties.PageSetupProperties ??= new S.PageSetupProperties();

            // ClosedXML retains the loaded fitToPage flag even after SetScale().
            worksheet.SheetProperties.PageSetupProperties.FitToPage = false;
            worksheet.Save();
        }
    }
}
