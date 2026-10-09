namespace ExcelRenderer.Core.Drawing;

/// <summary>Represents an extension-owned command at its original drawing position.</summary>
/// <param name="PageNumber">The source page number.</param>
internal sealed record CoreExtensionCommand(int PageNumber) : DrawCommand(PageNumber);
