using ExcelRenderer.Core.Rendering;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class CommandMetricTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void Commands_are_counted_lazily_even_when_consumption_stops_early(int consumed)
    {
        var previous = ConversionMetrics.Observer;
        var observations = new List<double>();
        var generated = 0;
        ConversionMetrics.Observer = (name, value) => { if (name == "commands") observations.Add(value); };
        try
        {
            var commands = ConversionMetrics.CountRenderedCommands(Source());
            Assert.Equal(0, generated);
            Assert.Empty(observations);
            Assert.Equal(Enumerable.Range(0, consumed), commands.Take(consumed));
            Assert.Equal(consumed, generated);
            Assert.Equal(consumed, Assert.Single(observations));
        }
        finally
        {
            ConversionMetrics.Observer = previous;
        }

        IEnumerable<int> Source()
        {
            for (var index = 0; index < 4; index++)
            {
                generated++;
                yield return index;
            }
        }
    }

    [Fact]
    public void Disabled_instrumentation_returns_the_original_source()
    {
        var previous = ConversionMetrics.Observer;
        ConversionMetrics.Observer = null;
        try
        {
            var source = Enumerable.Range(0, 4);
            Assert.Same(source, ConversionMetrics.CountRenderedCommands(source));
        }
        finally
        {
            ConversionMetrics.Observer = previous;
        }
    }
    [Fact]
    public void Page_scope_factory_receives_source_and_output_identity_and_is_optional()
    {
        var previous = ConversionMetrics.PageObserver;
        using var expected = new MemoryStream();
        ConversionMetrics.PageObserver = (sheet, source, output) =>
        {
            Assert.Equal("Example", sheet);
            Assert.Equal(5, source);
            Assert.Equal(2, output);
            return expected;
        };
        try
        {
            using (var scope = ConversionMetrics.BeginPage("Example", 5, 2))
            {
                Assert.Same(expected, scope);
            }

            Assert.False(expected.CanWrite);
            ConversionMetrics.PageObserver = null;
            Assert.Null(ConversionMetrics.BeginPage("Example", 5, 2));
        }
        finally
        {
            ConversionMetrics.PageObserver = previous;
        }
    }

}
