using System.Diagnostics;

namespace ExcelRenderer.Rendering;

/// <summary>Optional aggregate instrumentation; never adds cell diagnostics.</summary>
internal static class ConversionMetrics
{
    private static readonly AsyncLocal<Action<string, double>?> ObserverSlot = new();

    /// <summary>Gets or sets the observer in the current asynchronous conversion context.</summary>
    internal static Action<string, double>? Observer
    {
        get => ObserverSlot.Value;
        set => ObserverSlot.Value = value;
    }

    /// <summary>Reports an aggregate counter or snapshot.</summary>
    /// <param name="name">The metric name.</param>
    /// <param name="value">The metric value.</param>
    internal static void Report(string name, double value) => Observer?.Invoke(name, value);

    /// <summary>Measures a synchronous phase only when instrumentation is enabled.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="name">The phase name.</param>
    /// <param name="action">The operation to measure.</param>
    /// <returns>The operation result.</returns>
    internal static T Measure<T>(string name, Func<T> action)
    {
        if (Observer is null)
        {
            return action();
        }

        var timer = Stopwatch.StartNew();
        try
        {
            return action();
        }
        finally
        {
            Report(name + ".ms", timer.Elapsed.TotalMilliseconds);
        }
    }
}
