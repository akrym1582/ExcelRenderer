namespace ExcelRenderer.Layout;

/// <summary>Provides the shared half-open interval index to existing internal callers.</summary>
/// <typeparam name="T">The indexed value.</typeparam>
internal sealed class BandIndex<T>
{
    private readonly Core.Layout.BandIndex<T> inner;

    /// <summary>Initializes a new instance of the <see cref="BandIndex{T}"/> class.</summary>
    /// <param name="entries">The values and their intervals.</param>
    internal BandIndex(IEnumerable<(T Value, double Start, double End)> entries) => inner = new(entries);

    /// <summary>Queries origins in a half-open band.</summary>
    /// <param name="start">The inclusive start.</param>
    /// <param name="end">The exclusive end.</param>
    /// <returns>The selected values.</returns>
    internal IEnumerable<T> QueryStarts(double start, double end) => inner.QueryStarts(start, end);

    /// <summary>Queries intervals intersecting a half-open band.</summary>
    /// <param name="start">The inclusive start.</param>
    /// <param name="end">The exclusive end.</param>
    /// <returns>The selected values.</returns>
    internal IEnumerable<T> Query(double start, double end) => inner.Query(start, end);
}
