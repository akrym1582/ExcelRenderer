namespace ExcelRenderer.Core.Rendering;

/// <summary>Serializes PDFsharp global font state across the two conversion engines.</summary>
internal static class PdfSharpFontGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Acquires a synchronous font session.</summary>
    /// <param name="cancellationToken">The waiting cancellation token.</param>
    /// <returns>The session that releases the gate once.</returns>
    internal static Lease Acquire(CancellationToken cancellationToken = default)
    {
        Gate.Wait(cancellationToken);
        return new Lease();
    }

    /// <summary>Acquires an asynchronous font session.</summary>
    /// <param name="cancellationToken">The waiting cancellation token.</param>
    /// <returns>The session that releases the gate once.</returns>
    internal static async Task<Lease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease();
    }

    /// <summary>Owns one acquisition of the global font gate.</summary>
    internal sealed class Lease : IDisposable
    {
        private int disposed;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                Gate.Release();
            }
        }
    }
}
