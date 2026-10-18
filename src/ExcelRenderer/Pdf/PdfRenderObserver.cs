using ExcelRenderer.Drawing;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.Pdf;

/// <summary>Optional comparison instrumentation; never retains commands in normal conversions.</summary>
internal static class PdfRenderObserver
{
    private static readonly AsyncLocal<Action<RenderPageDescriptor, IEnumerable<DrawCommand>>?> Slot = new();
    private static readonly AsyncLocal<DateTime?> TimestampSlot = new();
    private static readonly AsyncLocal<Action<Stream>?> NativeSlot = new();

    /// <summary>Gets or sets the fixed comparison clock; ordinary conversions use their own snapshot.</summary>
    internal static DateTime? Timestamp
    {
        get => TimestampSlot.Value;
        set => TimestampSlot.Value = value;
    }

    /// <summary>Gets or sets the comparison observer of the original native PDF bytes.</summary>
    internal static Action<Stream>? NativePdf
    {
        get => NativeSlot.Value;
        set => NativeSlot.Value = value;
    }

    /// <summary>Gets or sets a caller-owned page fingerprint observer in this async flow.</summary>
    internal static Action<RenderPageDescriptor, IEnumerable<DrawCommand>>? Current
    {
        get => Slot.Value;
        set => Slot.Value = value;
    }
}
