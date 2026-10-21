using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ExcelRenderer;
using ExcelRenderer.Core.Pdf;
using ExcelRenderer.Core.Pdf.Structure;
using ExcelRenderer.OpenXml.Reader.Input;
using ExcelRenderer.Rendering;

/// <summary>Separates generated-PDF finalization from complete workbook conversion.</summary>
internal static class PdfReadBenchmark
{
    /// <summary>Measures fixed input bytes with optional stream diagnostics outside timing runs.</summary>
    /// <param name="args">Command, input, memory threshold, sample count and optional diagnostic flag.</param>
    /// <returns>The completed benchmark.</returns>
    internal static async Task RunAsync(string[] args)
    {
        var path = Path.GetFullPath(args[1]);
        var bytes = File.ReadAllBytes(path);
        var threshold = long.Parse(args[2], CultureInfo.InvariantCulture);
        var samples = int.Parse(args[3], CultureInfo.InvariantCulture);
        var diagnostic = args.Contains("--diagnostic", StringComparer.Ordinal);
        var workbook = path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
        var pages = workbook ? [] : new GeneratedPdfFile(new MemoryStream(bytes)).Pages.Select(_ => new GeneratedPdfFinalizer.PageCorrection(100.123, 99.456, [])).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Kind = "conditions",
            Input = Path.GetFileName(path),
            InputHash = Convert.ToHexString(SHA256.HashData(bytes)),
            InputBytes = bytes.Length,
            Threshold = threshold,
            Diagnostic = diagnostic,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Assemblies = new[] { typeof(GeneratedPdfFile).Assembly, typeof(SpillableBufferStream).Assembly, typeof(ExcelConverter).Assembly }.ToDictionary(a => a.GetName().Name!, a => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location)))),
        }));
        for (var iteration = -2; iteration < samples; iteration++)
        {
            using var spool = new SpillableBufferStream(new() { MemoryThresholdBytes = threshold, AllowTemporaryFiles = true });
            if (!workbook)
            {
                spool.Write(bytes);
                spool.Position = 0;
            }

            using var counted = diagnostic && !workbook ? new CountedInput(spool) : null;
            using var output = new DiscardOutput();
            double finalSave = 0;
            double spills = 0;
            ConversionMetrics.Observer = (name, value) =>
            {
                if (name == "pdfFinalSave.ms")
                {
                    finalSave += value;
                }
                else if (name == "buffer.spillCount")
                {
                    spills += value;
                }
            };
            using var input = new MemoryStream(bytes, writable: false);
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);
            var allocated = GC.GetTotalAllocatedBytes(true);
            var start = Stopwatch.GetTimestamp();
            try
            {
                if (workbook)
                {
                    await ExcelConverter.RenderAsync(input, Request(threshold), new SingleStreamOutputSink(output));
                }
                else
                {
                    GeneratedPdfFinalizer.Write(counted ?? (Stream)spool, output, pages, default);
                }
            }
            finally
            {
                ConversionMetrics.Observer = null;
            }

            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Kind = "sample",
                Iteration = iteration,
                Warmup = iteration < 0,
                ElapsedMs = elapsed,
                AllocatedBytes = allocatedBytes,
                GC = new[] { GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2 },
                FinalSaveMs = finalSave,
                Spills = workbook ? spills : spool.HasSpilled ? 1 : 0,
                OutputBytes = output.Bytes,
                Operations = counted?.Snapshot(),
            }));
        }

        if (diagnostic)
        {
            MeasureByteReads(threshold);
            using var output = File.Create(path + ".final.pdf");
            if (workbook)
            {
                using var input = new MemoryStream(bytes, writable: false);
                await ExcelConverter.RenderAsync(input, Request(threshold), new SingleStreamOutputSink(output));
            }
            else
            {
                using var spool = new SpillableBufferStream(new() { MemoryThresholdBytes = threshold, AllowTemporaryFiles = true });
                spool.Write(bytes);
                GeneratedPdfFinalizer.Write(spool, output, pages, default);
            }
        }
    }

    private static void MeasureByteReads(long threshold)
    {
        using var spool = new SpillableBufferStream(new() { MemoryThresholdBytes = threshold, AllowTemporaryFiles = true });
        spool.Write(new byte[131072]);
        for (var warmup = 0; warmup < 2; warmup++)
        {
            spool.Position = 0;
            while (spool.ReadByte() >= 0)
            {
            }
        }

        spool.Position = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        while (spool.ReadByte() >= 0)
        {
            count++;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine(JsonSerializer.Serialize(new { Kind = "byte-loop", Bytes = count, AllocatedBytes = allocated, Spilled = spool.HasSpilled }));
    }

    private static RenderRequest Request(long threshold) => new()
    {
        OutputFormat = OutputFormat.Pdf,
        Buffering = new() { MemoryThresholdBytes = threshold, AllowTemporaryFiles = true },
        FontOptions = new()
        {
            AllowSystemFonts = false,
            UseFontPack = false,
            Registrations = [new("Noto Sans JP", Path.GetFullPath("third_party/NotoSansJP/NotoSansJP-Regular.ttf"))],
            FallbackFamilies = ["Noto Sans JP"],
        },
    };

    /// <summary>Counts destination bytes without allocation growth or seeking.</summary>
    private sealed class DiscardOutput : Stream
    {
        /// <inheritdoc/>
        public override bool CanRead => false;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => true;

        /// <inheritdoc/>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        /// <summary>Gets the count written to the sink.</summary>
        internal long Bytes { get; private set; }

        /// <inheritdoc/>
        public override void Flush()
        {
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => Bytes += count;
    }

    /// <summary>Counts actual spool calls only during a separate diagnostic run.</summary>
    private sealed class CountedInput(Stream source) : Stream
    {
        private long reads;
        private long readBytes;
        private long readByteCalls;
        private long positionGets;
        private long positionSets;
        private long seeks;
        private long lengthGets;

        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => true;

        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override long Length
        {
            get
            {
                lengthGets++;
                return source.Length;
            }
        }

        /// <inheritdoc/>
        public override long Position
        {
            get
            {
                positionGets++;
                return source.Position;
            }

            set
            {
                positionSets++;
                source.Position = value;
            }
        }

        /// <inheritdoc/>
        public override void Flush()
        {
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            reads++;
            var read = source.Read(buffer, offset, count);
            readBytes += read;
            return read;
        }

        /// <inheritdoc/>
        public override int ReadByte()
        {
            readByteCalls++;
            var result = source.ReadByte();
            if (result >= 0)
            {
                readBytes++;
            }

            return result;
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            seeks++;
            return source.Seek(offset, origin);
        }

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <summary>Returns diagnostics after timing has ended.</summary>
        /// <returns>The independent operation totals.</returns>
        internal object Snapshot() => new { Reads = reads, ReadBytes = readBytes, ReadByteCalls = readByteCalls, PositionGets = positionGets, PositionSets = positionSets, Seeks = seeks, LengthGets = lengthGets };
    }
}
