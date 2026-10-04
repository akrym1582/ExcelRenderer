using ExcelRenderer.Rendering;

namespace ExcelRenderer.Tool.Commands;

/// <summary>Opens the CLI output file only after conversion preflight succeeds.</summary>
internal sealed class FileOutputSink : IRenderOutputSink
{
    private readonly string path;
    private Stream? stream;

    /// <summary>Initializes a new instance of the <see cref="FileOutputSink"/> class.</summary>
    /// <param name="path">The output file path.</param>
    internal FileOutputSink(string path) => this.path = path;

    /// <inheritdoc/>
    public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        return new(stream);
    }

    /// <inheritdoc/>
    public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken)
    {
        stream?.Dispose();
        stream = null;
        return default;
    }

    /// <inheritdoc/>
    public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken)
    {
        stream?.Dispose();
        stream = null;
        File.Delete(path);
        return default;
    }
}
