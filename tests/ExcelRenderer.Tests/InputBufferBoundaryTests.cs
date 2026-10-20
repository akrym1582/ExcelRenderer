using System.IO.Compression;
using ExcelRenderer.Rendering;
using Xunit;
using PreparedWorkbook = ExcelRenderer.OpenXml.Reader.Input.PreparedWorkbook;
using OperationMetrics = ExcelRenderer.OpenXml.Reader.Diagnostics.OperationMetrics;

namespace ExcelRenderer.Tests;

public sealed class InputBufferBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reader_and_main_input_settings_enforce_all_limits_and_preserve_ownership(bool core)
    {
        var directory = Directory.CreateTempSubdirectory();
        using var input = CreateInput();
        var settings = new WorkbookInputOptions
        {
            MemoryThresholdBytes = 1,
            MaxInputBytes = input.Length,
            AllowTemporaryFiles = true,
            TemporaryDirectory = directory.FullName,
            MaxZipEntryCount = 2,
            MaxUncompressedZipBytes = 200,
        };
        try
        {
            using (var owner = await Prepare(core, input, settings))
            {
                Assert.Single(directory.GetFiles());
                using var read = owner.OpenRead();
                Assert.Equal(input.Length, read.Length);
            }

            Assert.True(input.CanRead);
            Assert.Empty(directory.GetFiles());
            foreach (var rejected in new[]
            {
                settings with { MaxInputBytes = input.Length - 1 },
                settings with { MaxZipEntryCount = 1 },
                settings with { MaxUncompressedZipBytes = 199 },
                settings with { AllowTemporaryFiles = false },
            })
            {
                input.Position = 0;
                await Assert.ThrowsAsync<InvalidDataException>(() => Prepare(core, input, rejected));
                Assert.True(input.CanRead);
                Assert.Empty(directory.GetFiles());
            }

            input.Position = 0;
            using var memory = await Prepare(core, input, settings with { MemoryThresholdBytes = input.Length, AllowTemporaryFiles = false });
            using var first = memory.OpenRead();
            using var second = memory.OpenRead();
            Assert.Equal(first.ReadByte(), second.ReadByte());
            first.Position = first.Length;
            Assert.Equal(1, second.Position);
            Assert.Empty(directory.GetFiles());
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Input_and_output_spills_are_counted_once_in_each_async_context(bool core)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Run(1, ready, release);
        await ready.Task;
        var second = Run(2, release, ready);
        Assert.Equal(new[] { 2d, 2d }, await Task.WhenAll(first, second));

        async Task<double> Run(int writes, TaskCompletionSource signal, TaskCompletionSource wait)
        {
            var previous = ConversionMetrics.Observer;
            var previousOperation = OperationMetrics.Observer;
            var spills = 0d;
            var fileBytes = 0L;
            ConversionMetrics.Observer = (name, value) => { if (name == "buffer.spillCount") spills += value; };
            OperationMetrics.Observer = (name, value) => { if (name == "spool.fileBytes") fileBytes += value; };
            var directory = Directory.CreateTempSubdirectory();
            try
            {
                signal.SetResult();
                await wait.Task;
                using var input = CreateInput();
                using (var owner = await Prepare(core, input, new() { MemoryThresholdBytes = 1, AllowTemporaryFiles = true, TemporaryDirectory = directory.FullName }))
                {
                    Assert.Equal(1, spills);
                    using var read = owner.OpenRead();
                    Assert.Equal(input.Length, read.Length);
                }

                using (var output = core
                    ? Core.Input.InputBufferFactory.Create(new() { MemoryThresholdBytes = 1, AllowTemporaryFiles = true, TemporaryDirectory = directory.FullName })
                    : RenderBufferFactory.Create(new() { MemoryThresholdBytes = 1, AllowTemporaryFiles = true, TemporaryDirectory = directory.FullName }))
                {
                    for (var index = 0; index < writes; index++) output.Write(new byte[10]);
                    Assert.Single(directory.GetFiles());
                    Assert.Equal(2, spills);
                }

                Assert.Equal(input.Length + (10 * writes), fileBytes);
                Assert.Empty(directory.GetFiles());
                return spills;
            }
            finally
            {
                ConversionMetrics.Observer = previous;
                OperationMetrics.Observer = previousOperation;
                directory.Delete(true);
            }
        }
    }

    [Fact]
    public void Output_settings_apply_to_validation_and_created_buffers()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var settings = new RenderBufferOptions { MemoryThresholdBytes = 8, AllowTemporaryFiles = false, TemporaryDirectory = directory.FullName };
            settings.Validate();
            using var output = RenderBufferFactory.Create(settings);
            output.Write(new byte[8]);
            Assert.Throws<InvalidDataException>(() => output.WriteByte(1));
            Assert.Empty(directory.GetFiles());
            var invalid = settings with { MemoryThresholdBytes = -1 };
            Assert.Throws<ArgumentOutOfRangeException>(() => invalid.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => RenderBufferFactory.Create(invalid));
            var missing = settings with { TemporaryDirectory = Path.Combine(directory.FullName, "missing") };
            Assert.Throws<DirectoryNotFoundException>(() => missing.Validate());
            Assert.Throws<DirectoryNotFoundException>(() => RenderBufferFactory.Create(missing));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static Task<PreparedWorkbook> Prepare(bool core, Stream input, WorkbookInputOptions options) => core
        ? OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(input, new OpenXml.Reader.Input.WorkbookInputOptions
        {
            MemoryThresholdBytes = options.MemoryThresholdBytes,
            MaxInputBytes = options.MaxInputBytes,
            AllowTemporaryFiles = options.AllowTemporaryFiles,
            TemporaryDirectory = options.TemporaryDirectory,
            MaxZipEntryCount = options.MaxZipEntryCount,
            MaxUncompressedZipBytes = options.MaxUncompressedZipBytes,
        }, default, () => Core.Rendering.ConversionMetrics.Report("buffer.spillCount", 1))
        : Rendering.WorkbookInputPreparer.ReadAsync(input, options, default);

    private static MemoryStream CreateInput()
    {
        var input = new MemoryStream();
        using (var archive = new ZipArchive(input, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var index = 0; index < 2; index++)
            {
                using var entry = archive.CreateEntry(index + ".xml").Open();
                entry.Write(new byte[100]);
            }
        }

        input.Position = 0;
        return input;
    }
}
