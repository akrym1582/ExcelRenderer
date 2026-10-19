using System;
using System.Threading;

namespace ExcelRenderer.OpenXml.Reader.Diagnostics;

/// <summary>Conversion-local aggregate instrumentation for workbook storage validation.</summary>
internal static class OperationMetrics
{
    private static readonly AsyncLocal<Action<string, long>?> ObserverSlot = new AsyncLocal<Action<string, long>?>();

    /// <summary>Gets or sets the observer for the current asynchronous operation.</summary>
    internal static Action<string, long>? Observer
    {
        get => ObserverSlot.Value;
        set => ObserverSlot.Value = value;
    }

    /// <summary>Reports one aggregate measurement without retaining workbook data.</summary>
    /// <param name="name">Metric identity.</param>
    /// <param name="value">Observed count or byte size.</param>
    internal static void Report(string name, long value) => Observer?.Invoke(name, value);
}
