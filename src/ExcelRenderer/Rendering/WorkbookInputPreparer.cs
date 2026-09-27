using System.IO.Compression;
using System.Security.Cryptography;

namespace ExcelRenderer.Rendering;

internal static class WorkbookInputPreparer
{
    internal static async Task<byte[]> ReadAsync(Stream input, WorkbookInputOptions options, CancellationToken cancellationToken)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        if (options.MaxInputBytes <= 0 || options.MemoryThresholdBytes < 0 ||
            options.MaxZipEntryCount <= 0 || options.MaxUncompressedZipBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Input limits must be positive.");
        }

        // ClosedXML loads the complete workbook anyway. The memory threshold is therefore a policy
        // boundary: without explicitly permitting a temporary file, oversized inputs are rejected.
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total = checked(total + read);
            if (total > options.MaxInputBytes)
                throw new InvalidDataException($"Input exceeds the configured maximum of {options.MaxInputBytes} bytes.");
            if (total > options.MemoryThresholdBytes && !options.AllowTemporaryFiles)
                throw new InvalidDataException("Input exceeds MemoryThresholdBytes and temporary files are disabled.");
            await memory.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
        }

        var bytes = memory.ToArray();
        ValidateZip(bytes, options);
        return bytes;
    }

    private static void ValidateZip(byte[] bytes, WorkbookInputOptions options)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            if (archive.Entries.Count > options.MaxZipEntryCount)
                throw new InvalidDataException($"Workbook ZIP contains more than {options.MaxZipEntryCount} entries.");
            long uncompressed = 0;
            foreach (var entry in archive.Entries)
            {
                uncompressed = checked(uncompressed + entry.Length);
                if (uncompressed > options.MaxUncompressedZipBytes)
                    throw new InvalidDataException($"Workbook ZIP expands beyond {options.MaxUncompressedZipBytes} bytes.");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("Input is not a supported OOXML ZIP workbook.", exception);
        }
    }
}
