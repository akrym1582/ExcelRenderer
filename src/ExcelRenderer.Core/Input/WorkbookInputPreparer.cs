using System.IO.Compression;

namespace ExcelRenderer.Core.Input;

/// <summary>ClosedXML が読み込む前に、入力サイズと OOXML ZIP の安全性を検証します。</summary>
internal static class WorkbookInputPreparer
{
    /// <summary>入力ストリームを読み込み、上限を検証したブックの再読込可能な所有者を返します。</summary>
    /// <param name="input">現在位置から読み込む Excel ブックのストリームです。</param>
    /// <param name="options">入力サイズと ZIP 展開の上限を指定する設定です。</param>
    /// <param name="cancellationToken">読み込みのキャンセルを通知するトークンです。</param>
    /// <returns>検証済みの OOXML ZIP ブックの所有者を返します。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> が <see langword="null"/> の場合にスローされます。</exception>
    /// <exception cref="ArgumentOutOfRangeException">入力上限のいずれかが無効な場合にスローされます。</exception>
    /// <exception cref="InvalidDataException">入力サイズまたは ZIP 展開サイズが上限を超える場合、あるいは入力が OOXML ZIP でない場合にスローされます。</exception>
    /// <exception cref="OperationCanceledException">読み込みがキャンセルされた場合にスローされます。</exception>
    internal static async Task<PreparedWorkbook> ReadAsync(Stream input, WorkbookInputOptions options, CancellationToken cancellationToken)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (options.MaxInputBytes <= 0 || options.MemoryThresholdBytes < 0 ||
            options.MaxZipEntryCount <= 0 || options.MaxUncompressedZipBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Input limits must be positive.");
        }

        var memory = new SpillableBufferStream(new InputBufferOptions
        {
            MemoryThresholdBytes = options.MemoryThresholdBytes,
            AllowTemporaryFiles = options.AllowTemporaryFiles,
            TemporaryDirectory = options.TemporaryDirectory,
        });
        try
        {
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > options.MaxInputBytes)
                {
                    throw new InvalidDataException($"Input exceeds the configured maximum of {options.MaxInputBytes} bytes.");
                }

                if (total > options.MemoryThresholdBytes && !options.AllowTemporaryFiles)
                {
                    throw new InvalidDataException("Input exceeds MemoryThresholdBytes and temporary files are disabled.");
                }

                await memory.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            }

            using (var read = memory.OpenRead())
            {
                ValidateZip(read, options);
            }

            return new PreparedWorkbook(memory);
        }
        catch
        {
            memory.Dispose();
            throw;
        }
    }

    /// <summary>ブックの ZIP エントリ数と展開後サイズを検証します。</summary>
    /// <param name="bytes">検証するブックのストリームです。</param>
    /// <param name="options">ZIP の上限を指定する設定です。</param>
    /// <exception cref="InvalidDataException">入力が有効な ZIP でない場合、または上限を超える場合にスローされます。</exception>
    private static void ValidateZip(Stream bytes, WorkbookInputOptions options)
    {
        try
        {
            using var archive = new ZipArchive(bytes, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > options.MaxZipEntryCount)
            {
                throw new InvalidDataException($"Workbook ZIP contains more than {options.MaxZipEntryCount} entries.");
            }

            long uncompressed = 0;
            foreach (var entry in archive.Entries)
            {
                uncompressed = checked(uncompressed + entry.Length);
                if (uncompressed > options.MaxUncompressedZipBytes)
                {
                    throw new InvalidDataException($"Workbook ZIP expands beyond {options.MaxUncompressedZipBytes} bytes.");
                }
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
