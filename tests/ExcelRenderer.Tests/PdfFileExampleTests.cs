using ExcelRenderer.Http.Sample;
using UglyToad.PdfPig;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class PdfFileExampleTests
{
    [Fact]
    public async Task Main_file_sample_publishes_only_completed_pdf_and_preserves_existing_file_on_failure_or_cancel()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var input = Path.Combine(directory.FullName, "input.xlsx");
            var output = Path.Combine(directory.FullName, "report.pdf");
            await File.WriteAllBytesAsync(input, MainCoreParityTests.Fixture("portrait"));
            await PdfFileExample.ConvertAsync(input, output, MainCoreParityTests.FontPath);
            var completed = await File.ReadAllBytesAsync(output);
            using (var pdf = PdfDocument.Open(completed)) Assert.Equal(1, pdf.NumberOfPages);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PdfFileExample.ConvertAsync(input, output, MainCoreParityTests.FontPath, cancellation.Token));
            await File.WriteAllTextAsync(input, "invalid");
            await Assert.ThrowsAnyAsync<Exception>(() => PdfFileExample.ConvertAsync(input, output, MainCoreParityTests.FontPath));
            Assert.Equal(completed, await File.ReadAllBytesAsync(output));
            Assert.Equal(2, directory.GetFiles().Length);
        }
        finally { directory.Delete(true); }
    }
}
