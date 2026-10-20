namespace ExcelRenderer.PostScript;

/// <summary>Carries a stable diagnostic for unsupported PostScript content.</summary>
internal sealed class PostScriptFailure : NotSupportedException
{
    /// <summary>Initializes a new instance of the <see cref="PostScriptFailure"/> class.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="message">The failure reason.</param>
    internal PostScriptFailure(string code, string message)
        : base(message) => Code = code;

    /// <summary>Gets the stable diagnostic code.</summary>
    internal string Code { get; }
}
