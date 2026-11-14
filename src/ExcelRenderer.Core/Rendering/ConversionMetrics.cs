using System.Diagnostics;

namespace ExcelRenderer.Core.Rendering;

/// <summary>Optional aggregate instrumentation; never adds cell diagnostics.</summary>
internal static class ConversionMetrics
{
    private static readonly AsyncLocal<Action<string, double>?> ObserverSlot = new();
    private static readonly AsyncLocal<Func<string, int, int?, IDisposable?>?> PageObserverSlot = new();

    /// <summary>Gets or sets the observer in the current asynchronous conversion context.</summary>
    internal static Action<string, double>? Observer
    {
        get => ObserverSlot.Value;
        set => ObserverSlot.Value = value;
    }

    /// <summary>Gets or sets an optional rendering-page scope factory in the current conversion context.</summary>
    internal static Func<string, int, int?, IDisposable?>? PageObserver
    {
        get => PageObserverSlot.Value;
        set => PageObserverSlot.Value = value;
    }

    /// <summary>Starts an optional scope around actual page generation and drawing, excluding preflight.</summary>
    /// <param name="sheetName">The source sheet name.</param>
    /// <param name="sourcePage">The source sheet page number.</param>
    /// <param name="outputPage">The selected output page number, or null for a continuous canvas.</param>
    /// <returns>The observer-owned scope, or null when page logging is disabled.</returns>
    internal static IDisposable? BeginPage(string sheetName, int sourcePage, int? outputPage) => PageObserver?.Invoke(sheetName, sourcePage, outputPage);

    /// <summary>Reports an aggregate counter or snapshot.</summary>
    /// <param name="name">The metric name.</param>
    /// <param name="value">The metric value.</param>
    internal static void Report(string name, double value)
    {
        Observer?.Invoke(name, value);
    }

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

    /// <summary>Measures a phase through asynchronous completion, including failures and cancellation.</summary>
    /// <param name="name">The phase name.</param>
    /// <param name="action">The awaited operation.</param>
    /// <returns>The completion of the measured operation.</returns>
    internal static Task MeasureAsync(string name, Func<Task> action)
    {
        if (Observer is null)
        {
            return action();
        }

        return MeasureCoreAsync();

        async Task MeasureCoreAsync()
        {
            var timer = Stopwatch.StartNew();
            try
            {
                await action().ConfigureAwait(false);
            }
            finally
            {
                Report(name + ".ms", timer.Elapsed.TotalMilliseconds);
            }
        }
    }

    /// <summary>Measures deferred command generation separately from time spent consuming each command.</summary>
    /// <typeparam name="T">The command type.</typeparam>
    /// <param name="commands">The regenerable internal command source.</param>
    /// <returns>A source with measured MoveNext calls when instrumentation is enabled.</returns>
    internal static IEnumerable<T> MeasureCommands<T>(IEnumerable<T> commands)
    {
        if (Observer is null)
        {
            return commands;
        }

        return MeasureCommandCore(commands);
    }

    /// <summary>Counts commands generated for the rendering pass without buffering or counting preflight.</summary>
    /// <typeparam name="T">The command type.</typeparam>
    /// <param name="commands">The commands generated for output.</param>
    /// <returns>The original stream with a command-count observation when enumeration ends.</returns>
    internal static IEnumerable<T> CountRenderedCommands<T>(IEnumerable<T> commands) =>
        Observer is null ? commands : CountRenderedCommandCore(commands);

    private static IEnumerable<T> CountRenderedCommandCore<T>(IEnumerable<T> commands)
    {
        long count = 0;
        try
        {
            foreach (var command in commands)
            {
                count++;
                yield return command;
            }
        }
        finally
        {
            Report("commands", count);
        }
    }

    private static IEnumerable<T> MeasureCommandCore<T>(IEnumerable<T> commands)
    {
        using var iterator = commands.GetEnumerator();
        long elapsed = 0;
        try
        {
            while (true)
            {
                var start = Stopwatch.GetTimestamp();
                var available = iterator.MoveNext();
                elapsed += Stopwatch.GetTimestamp() - start;
                if (!available)
                {
                    break;
                }

                yield return iterator.Current;
            }
        }
        finally
        {
            Report("pageBuild.ms", elapsed * 1000d / Stopwatch.Frequency);
        }
    }
}
