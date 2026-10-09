namespace ExcelRenderer.Rendering;

/// <summary>Optional aggregate instrumentation; never adds cell diagnostics.</summary>
internal static class ConversionMetrics
{
    /// <summary>Gets or sets the observer in the current asynchronous conversion context.</summary>
    internal static Action<string, double>? Observer
    {
        get => Core.Rendering.ConversionMetrics.Observer;
        set => Core.Rendering.ConversionMetrics.Observer = value;
    }

    /// <summary>Reports an aggregate counter or snapshot.</summary>
    /// <param name="name">The metric name.</param>
    /// <param name="value">The metric value.</param>
    internal static void Report(string name, double value)
    {
        if (name == "pagePayloadActive")
        {
            RenderResourceSession.Current?.RecordPayload((int)value);
        }

        Core.Rendering.ConversionMetrics.Report(name, value);
    }

    /// <summary>Measures a synchronous phase only when instrumentation is enabled.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="name">The phase name.</param>
    /// <param name="action">The operation to measure.</param>
    /// <returns>The operation result.</returns>
    internal static T Measure<T>(string name, Func<T> action)
    {
        return Core.Rendering.ConversionMetrics.Measure(name, action);
    }

    /// <summary>Measures deferred command generation separately from time spent consuming each command.</summary>
    /// <param name="commands">The regenerable internal command source.</param>
    /// <returns>A source with measured MoveNext calls when instrumentation is enabled.</returns>
    internal static IEnumerable<ExcelRenderer.Drawing.DrawCommand> MeasureCommands(IEnumerable<ExcelRenderer.Drawing.DrawCommand> commands)
    => Core.Rendering.ConversionMetrics.MeasureCommands(commands);
}
