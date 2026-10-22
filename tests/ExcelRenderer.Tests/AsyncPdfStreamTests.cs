using ExcelRenderer.Rendering;
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
