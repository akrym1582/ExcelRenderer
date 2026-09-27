namespace ExcelRenderer.Rendering;

/// <summary>完了した変換生成物を記述します。</summary>
/// <param name="Descriptor">生成物の識別情報です。</param>
/// <param name="ByteLength">生成物へ書き込まれたバイト数です。</param>
public sealed record ArtifactMetadata(ArtifactDescriptor Descriptor, long ByteLength);
