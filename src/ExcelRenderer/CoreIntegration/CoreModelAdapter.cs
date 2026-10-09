using ExcelRenderer.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Maps neutral Core metadata at the public model boundary.</summary>
internal static partial class CoreModelAdapter
{
    /// <summary>Maps a HorizontalAlignment value explicitly.</summary>
    /// <param name="value">The source value.</param>
    /// <returns>The equivalent value.</returns>
    internal static Core.Model.HorizontalAlignment ToCore(HorizontalAlignment value) => value switch
    {
        HorizontalAlignment.Left => Core.Model.HorizontalAlignment.Left,
        HorizontalAlignment.Center => Core.Model.HorizontalAlignment.Center,
        HorizontalAlignment.Right => Core.Model.HorizontalAlignment.Right,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>Maps a HorizontalAlignment value explicitly.</summary>
    /// <param name="value">The source value.</param>
    /// <returns>The equivalent value.</returns>
    internal static HorizontalAlignment ToPublic(Core.Model.HorizontalAlignment value) => value switch
    {
        Core.Model.HorizontalAlignment.Left => HorizontalAlignment.Left,
        Core.Model.HorizontalAlignment.Center => HorizontalAlignment.Center,
        Core.Model.HorizontalAlignment.Right => HorizontalAlignment.Right,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>Maps a VerticalAlignment value explicitly.</summary>
    /// <param name="value">The source value.</param>
    /// <returns>The equivalent value.</returns>
    internal static Core.Model.VerticalAlignment ToCore(VerticalAlignment value) => value switch
    {
        VerticalAlignment.Top => Core.Model.VerticalAlignment.Top,
        VerticalAlignment.Center => Core.Model.VerticalAlignment.Center,
        VerticalAlignment.Bottom => Core.Model.VerticalAlignment.Bottom,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>Maps a VerticalAlignment value explicitly.</summary>
    /// <param name="value">The source value.</param>
    /// <returns>The equivalent value.</returns>
    internal static VerticalAlignment ToPublic(Core.Model.VerticalAlignment value) => value switch
    {
        Core.Model.VerticalAlignment.Top => VerticalAlignment.Top,
        Core.Model.VerticalAlignment.Center => VerticalAlignment.Center,
        Core.Model.VerticalAlignment.Bottom => VerticalAlignment.Bottom,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>Maps a BorderLineStyle value explicitly.</summary>
    /// <param name="value">The source value.</param>
    /// <returns>The equivalent value.</returns>
    internal static Core.Model.BorderLineStyle ToCore(BorderLineStyle value) => value switch
    {
        BorderLineStyle.Solid => Core.Model.BorderLineStyle.Solid,
        BorderLineStyle.Dotted => Core.Model.BorderLineStyle.Dotted,
        BorderLineStyle.Dashed => Core.Model.BorderLineStyle.Dashed,
        BorderLineStyle.DashDot => Core.Model.BorderLineStyle.DashDot,
        BorderLineStyle.DashDotDot => Core.Model.BorderLineStyle.DashDotDot,
        BorderLineStyle.Double => Core.Model.BorderLineStyle.Double,
        BorderLineStyle.Hair => Core.Model.BorderLineStyle.Hair,
        BorderLineStyle.SlantDashDot => Core.Model.BorderLineStyle.SlantDashDot,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>Maps a BorderLineStyle value explicitly.</summary>
    /// <param name="value">The source value.</param>
    /// <returns>The equivalent value.</returns>
    internal static BorderLineStyle ToPublic(Core.Model.BorderLineStyle value) => value switch
    {
        Core.Model.BorderLineStyle.Solid => BorderLineStyle.Solid,
        Core.Model.BorderLineStyle.Dotted => BorderLineStyle.Dotted,
        Core.Model.BorderLineStyle.Dashed => BorderLineStyle.Dashed,
        Core.Model.BorderLineStyle.DashDot => BorderLineStyle.DashDot,
        Core.Model.BorderLineStyle.DashDotDot => BorderLineStyle.DashDotDot,
        Core.Model.BorderLineStyle.Double => BorderLineStyle.Double,
        Core.Model.BorderLineStyle.Hair => BorderLineStyle.Hair,
        Core.Model.BorderLineStyle.SlantDashDot => BorderLineStyle.SlantDashDot,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>Maps a PrintScaleMode value explicitly.</summary>
    /// <param name="value">The source value.</param>
    /// <returns>The equivalent value.</returns>
    internal static Core.Model.PrintScaleMode ToCore(PrintScaleMode value) => value switch
    {
        PrintScaleMode.Explicit => Core.Model.PrintScaleMode.Explicit,
        PrintScaleMode.FitToPages => Core.Model.PrintScaleMode.FitToPages,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>Maps a PrintScaleMode value explicitly.</summary>
    /// <param name="value">The source value.</param>
    /// <returns>The equivalent value.</returns>
    internal static PrintScaleMode ToPublic(Core.Model.PrintScaleMode value) => value switch
    {
        Core.Model.PrintScaleMode.Explicit => PrintScaleMode.Explicit,
        Core.Model.PrintScaleMode.FitToPages => PrintScaleMode.FitToPages,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>Maps a PrintPageOrder value explicitly.</summary>
    /// <param name="value">The source value.</param>
    /// <returns>The equivalent value.</returns>
    internal static Core.Model.PrintPageOrder ToCore(PrintPageOrder value) => value switch
    {
        PrintPageOrder.DownThenOver => Core.Model.PrintPageOrder.DownThenOver,
        PrintPageOrder.OverThenDown => Core.Model.PrintPageOrder.OverThenDown,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>Maps a PrintPageOrder value explicitly.</summary>
    /// <param name="value">The source value.</param>
    /// <returns>The equivalent value.</returns>
    internal static PrintPageOrder ToPublic(Core.Model.PrintPageOrder value) => value switch
    {
        Core.Model.PrintPageOrder.DownThenOver => PrintPageOrder.DownThenOver,
        Core.Model.PrintPageOrder.OverThenDown => PrintPageOrder.OverThenDown,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>Maps ReportColor metadata without copying resource bytes.</summary>
    /// <param name="value">The source metadata.</param>
    /// <returns>The equivalent metadata.</returns>
    internal static Core.Model.ReportColor ToCore(Model.ReportColor value) => new(value.Red, value.Green, value.Blue, value.Alpha);

    /// <summary>Maps BorderSide metadata without copying resource bytes.</summary>
    /// <param name="value">The source metadata.</param>
    /// <returns>The equivalent metadata.</returns>
    internal static Core.Model.BorderSide ToCore(Model.BorderSide value) => new(value.Width, value.Color is { } color ? ToCore(color) : null, ToCore(value.LineStyle));

    /// <summary>Maps BorderStyle metadata without copying resource bytes.</summary>
    /// <param name="value">The source metadata.</param>
    /// <returns>The equivalent metadata.</returns>
    internal static Core.Model.BorderStyle ToCore(Model.BorderStyle value) => new(value.Left is { } left ? ToCore(left) : null, value.Top is { } top ? ToCore(top) : null, value.Right is { } right ? ToCore(right) : null, value.Bottom is { } bottom ? ToCore(bottom) : null);

    /// <summary>Maps FontStyle metadata without copying resource bytes.</summary>
    /// <param name="value">The source metadata.</param>
    /// <returns>The equivalent metadata.</returns>
    internal static Core.Model.FontStyle ToCore(Model.FontStyle value) => new(value.Family, value.Size, value.Underline, value.Color is { } color ? ToCore(color) : null)
    {
        Bold = value.Bold,
        Italic = value.Italic,
    };

    /// <summary>Maps IndexRange metadata without copying resource bytes.</summary>
    /// <param name="value">The source metadata.</param>
    /// <returns>The equivalent metadata.</returns>
    internal static Core.Model.IndexRange ToCore(Model.IndexRange value) => new(value.First, value.Last);

    /// <summary>Maps PageSettings metadata without copying resource bytes.</summary>
    /// <param name="value">The source metadata.</param>
    /// <returns>The equivalent metadata.</returns>
    internal static Core.Model.PageSettings ToCore(Model.PageSettings value) => new(
        value.Width,
        value.Height,
        value.MarginLeft,
        value.MarginTop,
        value.MarginRight,
        value.MarginBottom,
        value.Scale,
        value.FitToPagesWide,
        value.FitToPagesTall,
        value.TitleRows is { } rows ? ToCore(rows) : null,
        value.TitleColumns is { } columns ? ToCore(columns) : null)
    {
        ScaleMode = value.ScaleMode is { } mode ? ToCore(mode) : null,
        ManualRowBreaks = value.ManualRowBreaks,
        ManualColumnBreaks = value.ManualColumnBreaks,
        PageOrder = ToCore(value.PageOrder),
        HorizontalCentered = value.HorizontalCentered,
        VerticalCentered = value.VerticalCentered,
    };

    /// <summary>Maps CellStyle metadata without copying resource bytes.</summary>
    /// <param name="value">The source metadata.</param>
    /// <returns>The equivalent metadata.</returns>
    internal static Core.Model.CellStyle ToCore(Model.CellStyle value) => new(
        ToCore(value.Font),
        value.Background is { } background ? ToCore(background) : null,
        value.Border is { } border ? ToCore(border) : null,
        ToCore(value.HorizontalAlignment),
        ToCore(value.VerticalAlignment),
        value.WrapText,
        value.ShrinkToFit)
    {
        Indent = value.Indent,
        TextRotation = value.TextRotation,
        TopToBottom = value.TopToBottom,
    };

    /// <summary>Maps ReportColor metadata without copying resource bytes.</summary>
    /// <param name="value">The source metadata.</param>
    /// <returns>The equivalent metadata.</returns>
    internal static Model.ReportColor ToPublic(Core.Model.ReportColor value) => new(value.Red, value.Green, value.Blue, value.Alpha);

    /// <summary>Maps BorderSide metadata without copying resource bytes.</summary>
    /// <param name="value">The source metadata.</param>
    /// <returns>The equivalent metadata.</returns>
    internal static Model.BorderSide ToPublic(Core.Model.BorderSide value) => new(value.Width, value.Color is { } color ? ToPublic(color) : null, ToPublic(value.LineStyle));

    /// <summary>Maps BorderStyle metadata without copying resource bytes.</summary>
    /// <param name="value">The source metadata.</param>
    /// <returns>The equivalent metadata.</returns>
    internal static Model.BorderStyle ToPublic(Core.Model.BorderStyle value) => new(value.Left is { } left ? ToPublic(left) : null, value.Top is { } top ? ToPublic(top) : null, value.Right is { } right ? ToPublic(right) : null, value.Bottom is { } bottom ? ToPublic(bottom) : null);

    /// <summary>Maps FontStyle metadata without copying resource bytes.</summary>
    /// <param name="value">The source metadata.</param>
    /// <returns>The equivalent metadata.</returns>
    internal static Model.FontStyle ToPublic(Core.Model.FontStyle value) => new(value.Family, value.Size, value.Bold, value.Italic, value.Underline, value.Color is { } color ? ToPublic(color) : null);

    /// <summary>Maps IndexRange metadata without copying resource bytes.</summary>
    /// <param name="value">The source metadata.</param>
    /// <returns>The equivalent metadata.</returns>
    internal static Model.IndexRange ToPublic(Core.Model.IndexRange value) => new(value.First, value.Last);

    /// <summary>Maps PageSettings metadata without copying resource bytes.</summary>
    /// <param name="value">The source metadata.</param>
    /// <returns>The equivalent metadata.</returns>
    internal static Model.PageSettings ToPublic(Core.Model.PageSettings value) => new(
        value.Width,
        value.Height,
        value.MarginLeft,
        value.MarginTop,
        value.MarginRight,
        value.MarginBottom,
        value.Scale,
        value.FitToPagesWide,
        value.FitToPagesTall,
        value.TitleRows is { } rows ? ToPublic(rows) : null,
        value.TitleColumns is { } columns ? ToPublic(columns) : null)
    {
        ScaleMode = value.ScaleMode is { } mode ? ToPublic(mode) : null,
        ManualRowBreaks = value.ManualRowBreaks,
        ManualColumnBreaks = value.ManualColumnBreaks,
        PageOrder = ToPublic(value.PageOrder),
        HorizontalCentered = value.HorizontalCentered,
        VerticalCentered = value.VerticalCentered,
    };

    /// <summary>Maps CellStyle metadata without copying resource bytes.</summary>
    /// <param name="value">The source metadata.</param>
    /// <returns>The equivalent metadata.</returns>
    internal static Model.CellStyle ToPublic(Core.Model.CellStyle value) => new(
        ToPublic(value.Font),
        value.Background is { } background ? ToPublic(background) : null,
        value.Border is { } border ? ToPublic(border) : null,
        ToPublic(value.HorizontalAlignment),
        ToPublic(value.VerticalAlignment),
        value.WrapText,
        value.ShrinkToFit)
    {
        Indent = value.Indent,
        TextRotation = value.TextRotation,
        TopToBottom = value.TopToBottom,
    };
}
