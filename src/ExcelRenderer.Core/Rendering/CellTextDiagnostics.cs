using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Rendering;

/// <summary>Checks finalized logical cell text placement in output point coordinates.</summary>
internal static class CellTextDiagnostics
{
    /// <summary>Checks ordinary horizontal content clipping without remeasurement.</summary>
    /// <param name="layout">The finalized output layout.</param>
    /// <param name="bounds">The original cell content rectangle.</param>
    /// <param name="style">The effective cell style.</param>
    /// <returns>Whether visible logical text extends beyond ordinary cell clipping.</returns>
    internal static bool IsClipped(TextLayoutResult layout, ReportRect bounds, CellStyle style)
    {
        if ((!style.WrapText && !style.ShrinkToFit) || style.TextRotation != 0 || style.TopToBottom ||
            bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        const double tolerance = .25;
        foreach (var line in TextLayoutPlacement.Place(layout, bounds, style.HorizontalAlignment, style.VerticalAlignment))
        {
            if (string.IsNullOrEmpty(line.Line.Text))
            {
                continue;
            }

            if (line.Left < bounds.X - tolerance || line.Left + line.Line.Width > bounds.X + bounds.Width + tolerance ||
                line.Baseline - line.Line.Ascent < bounds.Y - tolerance ||
                line.Baseline + line.Line.Descent > bounds.Y + bounds.Height + tolerance)
            {
                return true;
            }
        }

        return false;
    }
}
