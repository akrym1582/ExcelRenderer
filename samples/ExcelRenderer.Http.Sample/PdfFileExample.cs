using ExcelRenderer.Rendering;

namespace ExcelRenderer.Http.Sample;

/// <summary>Publishes a completed main-library PDF only after closing the temporary output.</summary>
public static class PdfFileExample
{
    /// <summary>Converts a file, preserving an existing destination on conversion failure.</summary>
    /// <param name="inputPath">The input XLSX.</param>
    /// <param name="destinationPath">The final PDF path.</param>
    /// <param name="fontPath">The configured regular font.</param>
    /// <param name="token">Cancellation before publication.</param>
    /// <returns>The conversion result.</returns>
    public static async Task<ConversionResult> ConvertAsync(string inputPath, string destinationPath, string fontPath, CancellationToken token = default)
    {
        var destination = Path.GetFullPath(destinationPath);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            ConversionResult result;
            using (var input = File.OpenRead(inputPath))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                result = await ExcelConverter.RenderAsync(
                    input,
                    new RenderRequest
                    {
                        OutputFormat = OutputFormat.Pdf,
                        FontOptions = new() { FontFiles = [fontPath], AllowSystemFonts = false, UseFontPack = false },
                    },
                    new SingleStreamOutputSink(output),
                    token);
            }

            if (result.CompletionStatus != "Completed")
            {
                throw new InvalidOperationException("Conversion did not complete.");
            }

            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            return result;
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
