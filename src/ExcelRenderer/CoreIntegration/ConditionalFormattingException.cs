namespace ExcelRenderer.CoreIntegration;

/// <summary>Represents an indeterminate conditional rule, never a Boolean nonmatch.</summary>
internal sealed class ConditionalFormattingException(string code, string message) : Exception(message)
{
    /// <summary>Gets the stable diagnostic code.</summary>
    internal string Code { get; } = code;
}
