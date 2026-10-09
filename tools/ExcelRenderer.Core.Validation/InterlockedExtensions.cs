namespace CoreValidation;

/// <summary>Updates aggregate memory observations without forcing collection.</summary>
internal static class InterlockedExtensions
{
    /// <summary>Updates a concurrently observed maximum.</summary>
    /// <param name="target">The aggregate maximum.</param>
    /// <param name="value">The new observation.</param>
    internal static void UpdateMaximum(ref long target, long value)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref target);
            if (value <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }
}
