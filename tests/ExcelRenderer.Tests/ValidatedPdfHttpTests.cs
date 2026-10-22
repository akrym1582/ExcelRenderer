using ExcelRenderer.Http.Sample;
using ExcelRenderer.TestFixtures;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using Xunit;
using AsyncOnlyWriteStream = ExcelRenderer.Core.Tests.AsyncOnlyWriteStream;

namespace ExcelRenderer.Tests;

public sealed class ValidatedPdfHttpTests
{
    [Theory]
    [InlineData(false, "FormulaCachedValueMissing")]
    [InlineData(true, "FormulaCachedValueMissing")]
    [InlineData(false, "ImageDecodeFailed")]
    [InlineData(true, "ImageDecodeFailed")]
    public async Task Rejected_code_prevents_sending_pdf_and_removes_file(bool core, string code)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var workbook = Path.Combine(directory.FullName, "input.xlsx");
            if (code == "ImageDecodeFailed")
            {
                using var bytes = new MemoryStream();
                bytes.Write(MainCoreParityTests.Fixture("portrait"));
                using (var document = SpreadsheetDocument.Open(bytes, true))
                {
                    SdkWorkbook.Image(document.WorkbookPart!.WorksheetParts.First(), ImageReviewFixture.CorruptPng(), row: 1, column: 1);
                }
                await File.WriteAllBytesAsync(workbook, bytes.ToArray());
            }
            else await File.WriteAllBytesAsync(workbook, MainCoreParityTests.Fixture("values"));
            using var output = new MemoryStream();
            using var services = Services(out var logs);
            var context = Context(services, output);
            await PdfHttpEndpoints.HandleAsync(context, workbook, MainCoreParityTests.FontPath, core, true, directory.FullName);
            Assert.Equal(422, context.Response.StatusCode);
            var body = System.Text.Encoding.UTF8.GetString(output.ToArray());
            Assert.Contains(code, body);
            Assert.DoesNotContain("%PDF", body);
            Assert.DoesNotContain("saved", body);
            Assert.Contains(logs.Entries, entry => entry.Contains(code));
            Assert.Single(directory.GetFiles());
            Assert.True(output.CanWrite);
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nonrejected_diagnostic_returns_pdf_and_logs_code_without_cell_content(bool core)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var workbook = Path.Combine(directory.FullName, "input.xlsx");
            await File.WriteAllBytesAsync(workbook, MainCoreParityTests.Fixture("dimensions-mismatch"));
            using var output = new AsyncOnlyWriteStream();
            using var services = Services(out var logs);
            var context = Context(services, output);
            await PdfHttpEndpoints.HandleAsync(context, workbook, MainCoreParityTests.FontPath, core, true, directory.FullName);
            Assert.Equal(200, context.Response.StatusCode);
            using var pdf = PdfDocument.Open(output.Bytes);
            Assert.Equal(1, pdf.NumberOfPages);
            Assert.Contains(logs.Entries, entry => entry.Contains("MaximumDigitWidth"));
            Assert.DoesNotContain(logs.Entries, entry => entry.Contains("共通 ABC"));
            Assert.Single(directory.GetFiles());
            Assert.False(output.Disposed);
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData(false, "cancel-before")]
    [InlineData(true, "cancel-before")]
    [InlineData(false, "cancel-copy")]
    [InlineData(true, "cancel-copy")]
    [InlineData(false, "write-failure")]
    [InlineData(true, "write-failure")]
    public async Task All_copy_and_cancellation_paths_cleanup_without_error_json(bool core, string failure)
    {
        var directory = Directory.CreateTempSubdirectory();
        using var cancellation = new CancellationTokenSource();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var output = new AsyncOnlyWriteStream { Gate = failure == "cancel-copy" ? gate : null, FailWrite = failure == "write-failure" ? 1 : 0 };
        using var services = Services(out _);
        var context = Context(services, output);
        context.RequestAborted = cancellation.Token;
        // Once sending has begun, host policy owns errors: never append JSON.
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature(output));
        try
        {
            var workbook = Path.Combine(directory.FullName, "input.xlsx");
            await File.WriteAllBytesAsync(workbook, MainCoreParityTests.Fixture("portrait"));
            if (failure == "cancel-before") cancellation.Cancel();
            var task = PdfHttpEndpoints.HandleAsync(context, workbook, MainCoreParityTests.FontPath, core, true, directory.FullName);
            if (failure == "cancel-copy")
            {
                await output.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                cancellation.Cancel();
            }
            if (failure == "write-failure") await Assert.ThrowsAsync<IOException>(() => task);
            else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Single(directory.GetFiles());
            Assert.Empty(output.Bytes);
            Assert.False(output.Disposed);
        }
        finally { gate.TrySetResult(); directory.Delete(true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Server_owned_invalid_workbook_or_missing_font_is_500_without_details(bool core, bool missingFont)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var workbook = Path.Combine(directory.FullName, "private-input.xlsx");
            await File.WriteAllTextAsync(workbook, "not a ZIP");
            using var output = new MemoryStream();
            using var services = Services(out _);
            var context = Context(services, output);
            await PdfHttpEndpoints.HandleAsync(context, workbook, missingFont ? Path.Combine(directory.FullName, "private-font.ttf") : MainCoreParityTests.FontPath, core, true, directory.FullName);
            Assert.Equal(500, context.Response.StatusCode);
            var body = System.Text.Encoding.UTF8.GetString(output.ToArray());
            Assert.DoesNotContain("private-", body);
            Assert.DoesNotContain("Exception", body);
            Assert.Single(directory.GetFiles());
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task Parallel_requests_keep_files_and_diagnostics_separate()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var good = Path.Combine(directory.FullName, "good.xlsx");
            var rejected = Path.Combine(directory.FullName, "rejected.xlsx");
            await File.WriteAllBytesAsync(good, MainCoreParityTests.Fixture("portrait"));
            await File.WriteAllBytesAsync(rejected, MainCoreParityTests.Fixture("values"));
            await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            {
                using var output = new MemoryStream();
                using var services = Services(out _);
                var context = Context(services, output);
                await PdfHttpEndpoints.HandleAsync(context, index % 2 == 0 ? good : rejected, MainCoreParityTests.FontPath, index % 3 == 0, true, directory.FullName);
                Assert.Equal(index % 2 == 0 ? 200 : 422, context.Response.StatusCode);
            }));
            Assert.Equal(2, directory.GetFiles().Length);
        }
        finally { directory.Delete(true); }
    }

    private static DefaultHttpContext Context(IServiceProvider services, Stream output)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = output;
        return context;
    }

    private static ServiceProvider Services(out CapturedLogs logs)
    {
        logs = new CapturedLogs();
        var provider = logs;
        return new ServiceCollection().AddLogging(builder => builder.AddProvider(provider)).BuildServiceProvider();
    }

    private sealed class CapturedLogs : ILoggerProvider, ILogger
    {
        internal List<string> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Entries.Add(formatter(state, exception));
        public void Dispose() { }
    }

    private sealed class StartedResponseFeature(Stream stream) : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = stream;
        public bool HasStarted => true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
