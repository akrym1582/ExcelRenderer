using ExcelRenderer.Core.Rendering;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class ContractsTests
{
    [Fact]
    public async Task Null_and_missing_arguments_are_rejected()
    {
        using var input = TestSupport.Workbook();
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentNullException>(() => ExcelConverter.ConvertAsync(null!, output, TestSupport.Options));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ExcelConverter.ConvertAsync(input, null!, TestSupport.Options));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ExcelConverter.ConvertAsync(input, output, null!));
        await Assert.ThrowsAsync<ArgumentException>(() => ExcelConverter.ConvertAsync(input, output, new()));
        input.Position = 0;
        await Assert.ThrowsAsync<FileNotFoundException>(() => ExcelConverter.ConvertAsync(input, output, new() { FontFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".ttf") }));
    }

    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public async Task Stream_contracts_are_validated(bool readable, bool writable, bool seekable, bool nonempty)
    {
        using var source = TestSupport.Workbook();
        using var destination = new MemoryStream();
        if (nonempty) { destination.WriteByte(0); destination.Position = 0; }
        using var input = new TestStream(source, readable: readable);
        using var output = new TestStream(destination, writable: writable, seekable: seekable);
        await Assert.ThrowsAsync<ArgumentException>(() => ExcelConverter.ConvertAsync(input, output, TestSupport.Options));
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("ttcf")]
    [InlineData("OTTO")]
    [InlineData("wOFF")]
    public async Task Invalid_or_unsupported_fonts_fail_before_pages(string header)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".ttf");
        try
        {
            await File.WriteAllBytesAsync(path, System.Text.Encoding.ASCII.GetBytes(header.PadRight(32)));
            using var input = TestSupport.Workbook(); using var output = new MemoryStream();
            await Assert.ThrowsAsync<InvalidDataException>(() => ExcelConverter.ConvertAsync(input, output, new() { FontFilePath = path }));
            Assert.Equal(0, output.Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Missing_sheet_is_an_argument_error()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => TestSupport.Convert(options: TestSupport.Options with { SheetName = "missing" }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reads_current_position_and_keeps_caller_streams_open(bool seekable)
    {
        using var workbook = TestSupport.Workbook();
        using var prefixed = new MemoryStream();
        prefixed.Write(new byte[17]); workbook.CopyTo(prefixed); prefixed.Position = 17;
        using var target = new MemoryStream();
        var input = new TestStream(prefixed, seekable: seekable); var output = new TestStream(target);
        var result = await ExcelConverter.ConvertAsync(input, output, TestSupport.Options);
        Assert.Equal(1, result.PageCount); Assert.False(input.Disposed); Assert.False(output.Disposed);
        using var pdf = TestSupport.Open(target.ToArray()); Assert.Single(pdf.Pages);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("memory")]
    [InlineData("entry")]
    [InlineData("expanded")]
    public async Task Input_limits_are_enforced(string limit)
    {
        var settings = limit switch
        {
            "input" => new WorkbookInputOptions { MaxInputBytes = 1 },
            "memory" => new WorkbookInputOptions { MemoryThresholdBytes = 1 },
            "entry" => new WorkbookInputOptions { MaxZipEntryCount = 1 },
            _ => new WorkbookInputOptions { MaxUncompressedZipBytes = 1 },
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => TestSupport.Convert(options: TestSupport.Options with { Input = settings }));
    }

    [Fact]
    public async Task Spool_cleanup_on_success_and_cancellation()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(directory);
        try
        {
            var options = TestSupport.Options with { Input = new() { MemoryThresholdBytes = 1, AllowTemporaryFiles = true, TemporaryDirectory = directory } };
            await TestSupport.Convert(options: options); Assert.Empty(Directory.GetFiles(directory));
            using var input = TestSupport.Workbook(); using var output = new MemoryStream();
            using var cancellation = new CancellationTokenSource(); var reads = 0;
            using var cancelInput = new TestStream(input, onRead: () => { if (++reads == 2) cancellation.Cancel(); });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExcelConverter.ConvertAsync(cancelInput, output, options, cancellation.Token));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Write_failure_releases_the_gate_and_native_resources()
    {
        var created = 0; var disposed = 0;
        ConversionMetrics.Observer = (key, value) => { if (key == "typefaceCreated") created++; if (key == "typefaceDisposed") disposed++; };
        try
        {
            using var input = TestSupport.Workbook(); using var output = new MemoryStream();
            using var failing = new TestStream(output, onWrite: () => throw new IOException("write failed"));
            await Assert.ThrowsAsync<IOException>(() => ExcelConverter.ConvertAsync(input, failing, TestSupport.Options));
            Assert.Equal(1, (await TestSupport.Convert()).Result.PageCount);
            Assert.Equal(2, created); Assert.Equal(created, disposed);
        }
        finally { ConversionMetrics.Observer = null; }
    }

    [Fact]
    public async Task Font_table_bounds_and_variable_fonts_are_rejected()
    {
        foreach (var variable in new[] { false, true })
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".data");
            var bytes = await File.ReadAllBytesAsync(TestSupport.JapaneseFont);
            if (variable) System.Text.Encoding.ASCII.GetBytes("fvar").CopyTo(bytes, 12);
            else for (var i = 24; i < 28; i++) bytes[i] = 255;
            try
            {
                await File.WriteAllBytesAsync(path, bytes);
                await Assert.ThrowsAsync<InvalidDataException>(() => TestSupport.Convert(options: TestSupport.Options with { FontFilePath = path }));
            }
            finally { File.Delete(path); }
        }
    }

    [Fact]
    public async Task Invalid_workbook_and_pre_cancelled_request_leave_output_empty()
    {
        using var input = new MemoryStream(new byte[] { 1, 2, 3 }); using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => ExcelConverter.ConvertAsync(input, output, TestSupport.Options));
        using var workbook = TestSupport.Workbook(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExcelConverter.ConvertAsync(workbook, output, TestSupport.Options, cancellation.Token));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void Exported_types_and_dependencies_are_minimal()
    {
        var assembly = typeof(ExcelConverter).Assembly;
        Assert.Equal(new[] { "ConversionDiagnostic", "ConversionResult", "ExcelConverter", "PdfExportOptions", "WorkbookInputOptions" }, assembly.GetExportedTypes().Select(t => t.Name).Order().ToArray());
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a => a.Name is "ExcelRenderer" or "ExcelRenderer.Fonts" or "ExcelRenderer.Tool");
    }
}
