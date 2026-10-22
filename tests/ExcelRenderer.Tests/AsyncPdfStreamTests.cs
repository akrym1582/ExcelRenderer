using ExcelRenderer.Rendering;
using ExcelRenderer.Http.Sample;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;
using Xunit;
using AsyncOnlyWriteStream = ExcelRenderer.Core.Tests.AsyncOnlyWriteStream;

namespace ExcelRenderer.Tests;

public sealed class AsyncPdfStreamTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Main_awaits_async_only_output_flushes_once_and_counts_successful_bytes(bool spill)
    {
        var directory = Directory.CreateTempSubdirectory();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var input = Input();
        using var output = new AsyncOnlyWriteStream { Gate = gate };
        try
        {
            var task = ExcelConverter.RenderAsync(input, Request(spill, directory.FullName), new SingleStreamOutputSink(output));
            await output.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(task.IsCompleted);
            Assert.Equal(1, output.Writes);
            Assert.Empty(output.Bytes);
            gate.SetResult();
            var result = await task;
            Assert.Equal(output.Bytes.LongLength, Assert.Single(result.Artifacts).ByteLength);
            Assert.Equal(1, output.Flushes);
            Assert.Equal(1, output.MaxActive);
            Assert.False(output.Disposed);
            Assert.True(input.CanRead);
            Assert.Empty(directory.GetFiles());
            using var pdf = PdfDocument.Open(output.Bytes, new ParsingOptions { UseLenientParsing = false });
            Assert.Equal(result.Pages.Count, pdf.NumberOfPages);
            Assert.NotEmpty(pdf.GetPage(1).Text);
        }
        finally { gate.TrySetResult(); directory.Delete(true); }
    }

    [Theory]
    [InlineData("write", false)]
    [InlineData("write", true)]
    [InlineData("flush", true)]
    [InlineData("complete", true)]
    [InlineData("open", true)]
    public async Task Failures_abort_only_opened_artifacts_and_preserve_the_original_error(string failure, bool spill)
    {
        var directory = Directory.CreateTempSubdirectory();
        using var input = Input();
        using var output = new AsyncOnlyWriteStream { FailWrite = failure == "write" ? 2 : 0, FailFlush = failure == "flush" };
        var sink = new TrackingSink(output) { FailOpen = failure == "open", FailComplete = failure == "complete", FailAbort = true };
        try
        {
            var error = await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, Request(spill, directory.FullName), sink));
            Assert.Empty(error.CompletedArtifacts);
            var original = Assert.IsType<IOException>(error.InnerException);
            Assert.Equal(failure + " failure", original.Message);
            Assert.Equal(failure == "open" ? 0 : 1, sink.Aborts);
            Assert.Equal(failure == "complete" ? 1 : 0, sink.Completes);
            if (failure != "open")
            {
                Assert.Same(original, sink.Error);
                Assert.Equal("abort failure", Assert.IsType<IOException>(original.Data["AbortException"]).Message);
            }
            Assert.False(output.Disposed);
            Assert.True(input.CanRead);
            Assert.Empty(directory.GetFiles());
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_output_notifies_abort_with_a_cleanup_token_and_releases_spool(bool spill)
    {
        var directory = Directory.CreateTempSubdirectory();
        using var input = Input();
        using var cancellation = new CancellationTokenSource();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var output = new AsyncOnlyWriteStream { Gate = gate };
        var sink = new TrackingSink(output);
        try
        {
            var task = ExcelConverter.RenderAsync(input, Request(spill, directory.FullName), sink, cancellation.Token);
            await output.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Equal(1, sink.Aborts);
            Assert.False(sink.AbortToken.CanBeCanceled);
            Assert.Equal(0, sink.Completes);
            Assert.False(output.Disposed);
            Assert.True(input.CanRead);
            Assert.Empty(directory.GetFiles());
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData("/pdf")]
    [InlineData("/core-pdf")]
    [InlineData("/pdf-validated")]
    [InlineData("/core-pdf-validated")]
    [InlineData("/pdf-validated", true)]
    [InlineData("/core-pdf-validated", true)]
    public async Task Kestrel_response_body_accepts_direct_pdf_with_synchronous_io_disabled(string endpoint, bool rejected = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.AllowSynchronousIO = false).UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var handled = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Use(async (context, next) =>
        {
            try
            {
                Assert.False(context.Features.Get<IHttpBodyControlFeature>()!.AllowSynchronousIO);
                Assert.False(context.Response.Body.CanSeek);
                await next(context);
                handled.TrySetResult(null);
            }
            catch (Exception error) { handled.TrySetResult(error); throw; }
        });
        var directory = Directory.CreateTempSubdirectory();
        var workbook = Path.Combine(directory.FullName, "input.xlsx");
        await File.WriteAllBytesAsync(workbook, MainCoreParityTests.Fixture(rejected ? "values" : "portrait"));
        PdfHttpEndpoints.Map(app, workbook, FontPath);
        await app.StartAsync();
        try
        {
            var address = Assert.Single(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var response = await client.GetAsync(address + endpoint);
            Assert.Null(await handled.Task.WaitAsync(TimeSpan.FromSeconds(15)));
            if (rejected)
            {
                Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.Contains("FormulaCachedValueMissing", body);
                Assert.DoesNotContain("%PDF", body);
                return;
            }
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
            using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync(), new ParsingOptions { UseLenientParsing = false });
            Assert.True(pdf.NumberOfPages > 0);
            Assert.NotEmpty(pdf.GetPage(1).Text);
        }
        finally { await app.StopAsync(); directory.Delete(true); }
    }

    private static string FontPath => Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf");
    private static FileStream Input() => File.OpenRead(Path.Combine(SampleOutputTestSupport.InputDirectory, "01-japanese.xlsx"));
    private static RenderRequest Request(bool spill, string directory) => new()
    {
        OutputFormat = OutputFormat.Pdf,
        FontOptions = new() { FontFiles = [FontPath], AllowSystemFonts = false, UseFontPack = false },
        Buffering = new() { MemoryThresholdBytes = spill ? 1 : 8 * 1024 * 1024, AllowTemporaryFiles = spill, TemporaryDirectory = directory },
    };

    private sealed class TrackingSink(Stream stream) : IRenderOutputSink
    {
        internal bool FailOpen { get; init; }
        internal bool FailComplete { get; init; }
        internal bool FailAbort { get; init; }
        internal int Aborts { get; private set; }
        internal int Completes { get; private set; }
        internal Exception? Error { get; private set; }
        internal CancellationToken AbortToken { get; private set; }
        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken token) => FailOpen ? ValueTask.FromException<Stream>(new IOException("open failure")) : ValueTask.FromResult(stream);
        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long length, CancellationToken token)
        {
            Completes++;
            return FailComplete ? ValueTask.FromException(new IOException("complete failure")) : ValueTask.CompletedTask;
        }
        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Aborts++;
            Error = error;
            AbortToken = token;
            return FailAbort ? ValueTask.FromException(new IOException("abort failure")) : ValueTask.CompletedTask;
        }
    }
}
