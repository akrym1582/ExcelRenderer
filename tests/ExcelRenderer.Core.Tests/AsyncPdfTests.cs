using ExcelRenderer.Core.Pdf;
using ExcelRenderer.Core.Rendering;
using ExcelRenderer.OpenXml.Reader.Input;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class AsyncPdfTests
{
    [Fact]
    public async Task Core_awaits_nonseekable_output_without_flushing_or_disposing_it()
    {
        using var input = TestSupport.Workbook();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var output = new AsyncOnlyWriteStream { Gate = gate };
        var saved = false;
        ConversionMetrics.Observer = (name, _) => { if (name == "pdfFinalSave.ms") saved = true; };
        try
        {
            var task = ExcelConverter.ConvertAsync(input, output, TestSupport.Options);
            await output.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(task.IsCompleted);
            Assert.False(saved);
            Assert.Equal(1, output.Writes);
            Assert.Empty(output.Bytes);
            gate.SetResult();
            Assert.Equal(1, (await task).PageCount);
            Assert.True(saved);
            Assert.Equal(1, output.MaxActive);
            Assert.Equal(0, output.Flushes);
            Assert.False(output.Disposed);
            Assert.True(input.CanRead);
            using var pdf = TestSupport.Open(output.Bytes);
            Assert.Contains("abc", pdf.GetPage(1).Text);
        }
        finally { gate.TrySetResult(); ConversionMetrics.Observer = null; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Core_write_failure_or_cancellation_keeps_borrowed_streams_open(bool cancel)
    {
        using var input = TestSupport.Workbook();
        using var cancellation = new CancellationTokenSource();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var output = new AsyncOnlyWriteStream { Gate = cancel ? gate : null, FailWrite = cancel ? 0 : 2 };
        var task = ExcelConverter.ConvertAsync(input, output, TestSupport.Options, cancellation.Token);
        if (cancel)
        {
            await output.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        }
        else await Assert.ThrowsAsync<IOException>(() => task);
        Assert.False(output.Disposed);
        Assert.True(input.CanRead);
        Assert.Equal(1, (await TestSupport.Convert()).Result.PageCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Document_releases_memory_or_file_spool_on_success_or_failure(bool spill, bool fail)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using var output = new AsyncOnlyWriteStream { FailWrite = fail ? 1 : 0 };
            using (var document = new SkiaPdfDocument(new InputBufferOptions
            {
                MemoryThresholdBytes = spill ? 1 : 1024 * 1024,
                AllowTemporaryFiles = spill,
                TemporaryDirectory = directory.FullName,
            }, default))
            {
                document.Prepare();
                document.Append(new(100.123, 99.456, []), _ => { });
                if (fail) await Assert.ThrowsAsync<IOException>(() => document.CompleteAsync(output));
                else await document.CompleteAsync(output);
                Assert.Throws<InvalidOperationException>(() => document.Complete(output));
                await Assert.ThrowsAsync<InvalidOperationException>(() => document.CompleteAsync(output));
                Assert.Equal(spill ? 1 : 0, directory.GetFiles().Length);
            }
            Assert.Empty(directory.GetFiles());
            Assert.False(output.Disposed);
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task Wrapper_forwards_both_async_write_overloads_and_both_tokens(int method, bool cancelConversion)
    {
        using var conversion = new CancellationTokenSource();
        using var call = new CancellationTokenSource();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var output = new AsyncOnlyWriteStream { Gate = gate };
        using (var wrapper = new CancellationWriteStream(output, conversion.Token))
        {
            if (method == 2)
            {
                await wrapper.FlushAsync(call.Token);
                Assert.Equal(1, output.Flushes);
                (cancelConversion ? conversion : call).Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wrapper.FlushAsync(call.Token));
            }
            else
            {
                var task = method == 0 ? wrapper.WriteAsync(new byte[1], 0, 1, call.Token) : wrapper.WriteAsync(new byte[1].AsMemory(), call.Token).AsTask();
                await output.Entered.Task;
                (cancelConversion ? conversion : call).Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            }
        }
        Assert.False(output.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Async_metrics_report_only_after_completion_and_also_on_failure(bool fail)
    {
        var observed = false;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConversionMetrics.Observer = (name, _) => { if (name == "test.ms") observed = true; };
        try
        {
            var task = ConversionMetrics.MeasureAsync("test", async () => { await gate.Task; if (fail) throw new IOException(); });
            Assert.False(observed);
            gate.SetResult();
            if (fail) await Assert.ThrowsAsync<IOException>(() => task);
            else await task;
            Assert.True(observed);
        }
        finally { ConversionMetrics.Observer = null; }
    }
}
