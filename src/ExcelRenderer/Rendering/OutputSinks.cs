using ExcelRenderer.Markdown;
using ExcelRenderer.Model;

namespace ExcelRenderer.Rendering;

/// <summary>Writes one artifact to a caller-owned stream without closing it.</summary>
public sealed class SingleStreamOutputSink : IRenderOutputSink
{
    private readonly Stream _stream;
    private bool _opened;

    public SingleStreamOutputSink(Stream stream) => _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken)
    {
        if (_opened) throw new InvalidOperationException("A single-stream sink accepts exactly one artifact.");
        if (!_stream.CanWrite) throw new ArgumentException("The output stream must be writable.");
        _opened = true;
        return new(_stream);
    }

    public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) => default;
    public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) => default;
}

/// <summary>Creates a new file only when the renderer opens its single artifact.</summary>
internal sealed class NewFileOutputSink : IRenderOutputSink
{
    private readonly string _path;
    private Stream? _stream;

    internal NewFileOutputSink(string path) => _path = path ?? throw new ArgumentNullException(nameof(path));

    public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        _stream = new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        return new(_stream);
    }

    public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken)
    {
        _stream?.Dispose();
        _stream = null;
        return default;
    }

    public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken)
    {
        _stream?.Dispose();
        _stream = null;
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        return default;
    }
}

internal interface IMarkdownDocumentOutputSink
{
    Task WriteMarkdownAsync(ReportDocument document, CancellationToken cancellationToken);
    long GetMarkdownByteLength();
}

internal sealed class LegacyMarkdownOutputSink : IRenderOutputSink, IMarkdownDocumentOutputSink
{
    private readonly string _outputPath;
    private readonly MarkdownExportOptions _options;
    private readonly string _documentName;

    internal LegacyMarkdownOutputSink(string outputPath, MarkdownExportOptions options, string documentName)
    {
        _outputPath = outputPath;
        _options = options;
        _documentName = documentName;
    }

    public async Task WriteMarkdownAsync(ReportDocument document, CancellationToken cancellationToken) =>
        await new MarkdownExporter().ExportToFileAsync(document, _outputPath, _options, _documentName, cancellationToken)
            .ConfigureAwait(false);

    public long GetMarkdownByteLength() => new FileInfo(_outputPath).Length;

    public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The legacy Markdown adapter writes the complete document directly.");

    public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) => default;
    public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) => default;
}

/// <summary>Stores artifacts below a directory without overwriting existing files.</summary>
public sealed class DirectoryOutputSink : IRenderOutputSink
{
    private readonly string _root;
    private readonly Dictionary<string, Stream> _streams = new(StringComparer.Ordinal);

    public DirectoryOutputSink(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("An output directory is required.", nameof(root));
        _root = Path.GetFullPath(root);
    }

    public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken)
    {
        var path = GetPath(artifact.RelativeName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        _streams.Add(artifact.ArtifactId, stream);
        return new(stream);
    }

    public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken)
    {
        Close(artifact.ArtifactId);
        return default;
    }

    public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken)
    {
        var path = GetPath(artifact.RelativeName);
        Close(artifact.ArtifactId);
        if (File.Exists(path)) File.Delete(path);
        return default;
    }

    private string GetPath(string relativeName)
    {
        if (string.IsNullOrWhiteSpace(relativeName) || Path.IsPathRooted(relativeName)) throw new ArgumentException("Artifact name must be a relative path.");
        var path = Path.GetFullPath(Path.Combine(_root, relativeName));
        var prefix = _root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? _root : _root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Artifact path must remain below the output directory.");
        return path;
    }

    private void Close(string artifactId)
    {
        if (_streams.TryGetValue(artifactId, out var stream))
        {
            _streams.Remove(artifactId);
            stream.Dispose();
        }
    }
}
