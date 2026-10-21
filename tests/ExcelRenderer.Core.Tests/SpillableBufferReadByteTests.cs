using ExcelRenderer.OpenXml.Reader.Input;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class SpillableBufferReadByteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadByte_preserves_unsigned_values_mixed_reads_and_independent_cursors(bool spill)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using (var stream = new SpillableBufferStream(new()
            {
                MemoryThresholdBytes = spill ? 1 : 1024,
                AllowTemporaryFiles = true,
                TemporaryDirectory = directory.FullName,
            }))
            {
                byte[] data = [0, 127, 128, 255, 42];
                stream.Write(data);
                stream.Position = 0;
                Assert.Equal(spill, stream.HasSpilled);
                using var independent = stream.OpenRead();
                foreach (var value in data)
                {
                    Assert.Equal((int)value, stream.ReadByte());
                }

                Assert.Equal(-1, stream.ReadByte());
                Assert.Equal(-1, stream.ReadByte());
                Assert.Equal(data.Length, stream.Position);
                Assert.Equal(0, independent.Position);
                Assert.Equal(0, independent.ReadByte());
                Assert.Equal(2, stream.Seek(2, SeekOrigin.Begin));
                Assert.Equal(128, stream.ReadByte());
                var pair = new byte[2];
                Assert.Equal(2, stream.Read(pair, 0, 2));
                Assert.Equal(new byte[] { 255, 42 }, pair);
                stream.Position = 1;
                Assert.Equal(127, stream.ReadByte());
                Assert.Equal(1, independent.Position);
            }

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
    public void ReadByte_does_not_allocate_per_byte_after_warmup(bool spill)
    {
        var bytes = new byte[131072];
        using var stream = new SpillableBufferStream(new() { MemoryThresholdBytes = spill ? 1 : bytes.Length, AllowTemporaryFiles = true });
        stream.Write(bytes);
        for (var round = 0; round < 2; round++)
        {
            stream.Position = 0;
            while (stream.ReadByte() >= 0) { }
        }

        stream.Position = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var read = 0;
        while (stream.ReadByte() >= 0)
        {
            read++;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(bytes.Length, read);
        Assert.InRange(allocated, 0, 1024);
    }
}
