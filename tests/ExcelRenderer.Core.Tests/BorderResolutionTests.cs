using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;
using Xunit;

namespace ExcelRenderer.Core.Tests;

/// <summary>Fixes the deterministic fallback contract independently of Excel availability.</summary>
public sealed class BorderResolutionTests
{
    public static IEnumerable<object[]> StylePairs =>
        from first in Enum.GetValues<BorderLineStyle>()
        from second in Enum.GetValues<BorderLineStyle>()
        from vertical in new[] { false, true }
        select new object[] { (int)first, (int)second, vertical };

    [Theory]
    [MemberData(nameof(StylePairs))]
    public void Fallback_is_deterministic_for_all_style_pairs(int firstValue, int secondValue, bool vertical)
    {
        var firstKind = (BorderLineStyle)firstValue;
        var secondKind = (BorderLineStyle)secondValue;
        var first = new BorderSide(0.75, new(255, 255, 255), firstKind);
        var second = new BorderSide(0.75, new(0, 0, 0), secondKind);
        var cells = new[]
        {
            Cell(new(0, 0, 20, 20), vertical ? new(Right: first) : new(Bottom: first)),
            Cell(vertical ? new(20, 0, 20, 20) : new(0, 20, 20, 20), vertical ? new(Left: second) : new(Top: second)),
        };
        var command = Assert.IsType<DrawLineCommand>(Assert.Single(BorderConflictResolver.Generate(1, cells)));
        var rank = new[] { BorderLineStyle.Hair, BorderLineStyle.Dotted, BorderLineStyle.Dashed, BorderLineStyle.SlantDashDot, BorderLineStyle.DashDot, BorderLineStyle.DashDotDot, BorderLineStyle.Solid, BorderLineStyle.Double };
        var expected = Array.IndexOf(rank, firstKind) >= Array.IndexOf(rank, secondKind) ? first : second;
        Assert.Equal(expected, command.Style);
        Assert.Equal(BorderConflictResolver.Generate(1, cells), BorderConflictResolver.Generate(1, cells.Reverse()));
    }

    [Theory]
    [InlineData((int)BorderLineStyle.Double, 0.75, (int)BorderLineStyle.Solid, 3, (int)BorderLineStyle.Double)]
    [InlineData((int)BorderLineStyle.Solid, 0.5, (int)BorderLineStyle.Dotted, 1.5, (int)BorderLineStyle.Dotted)]
    [InlineData((int)BorderLineStyle.Solid, 3, (int)BorderLineStyle.Double, 0.75, (int)BorderLineStyle.Double)]
    public void Logical_double_priority_is_separate_from_stroke_width(int firstKind, double firstWidth, int secondKind, double secondWidth, int expected)
    {
        var commands = BorderConflictResolver.Generate(1,
        [Cell(new(0, 0, 20, 20), new(Right: new(firstWidth, LineStyle: (BorderLineStyle)firstKind))), Cell(new(20, 0, 20, 20), new(Left: new(secondWidth, LineStyle: (BorderLineStyle)secondKind)))]);
        Assert.Equal((BorderLineStyle)expected, Assert.IsType<DrawLineCommand>(Assert.Single(commands)).Style.LineStyle);
    }

    [Fact]
    public void Placements_with_different_clips_remain_independent()
    {
        var first = Cell(new(10, 10, 20, 20), new(Right: new())) with { ClipBounds = new(0, 0, 40, 40) };
        var second = first with { ClipBounds = new(20, 0, 40, 40) };
        Assert.Equal(2, BorderConflictResolver.Generate(1, [first, second]).Count());
    }

    [Fact]
    public void Floating_point_placement_noise_does_not_duplicate_an_edge()
    {
        var cells = new[] { Cell(new(0, 0, 0.1 + 0.2, 10), new(Right: new())), Cell(new(0.3, 0, 10, 10), new(Left: new())) };
        Assert.Single(BorderConflictResolver.Generate(1, cells));
    }

    [Fact]
    public void Repeated_neighbor_queries_do_not_allocate_iterator_stacks()
    {
        var index = new BandIndex<int>(Enumerable.Range(0, 1000).Select(value => (value, (double)value, value + 1d)));
        var sum = 0;
        Action<int> visitor = value => sum += value;
        index.Visit(450, 451, visitor);
        sum = 0;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 200; iteration++)
        {
            index.Visit(450, 451, visitor);
        }

        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Equal(90000, sum);
        Assert.Equal(0, allocated);
        index.Visit(450, 450, visitor);
        Assert.Equal(90000, sum);
    }

    [Fact]
    public void Neighbor_visitor_keeps_nested_intervals_and_half_open_ends()
    {
        var index = new BandIndex<string>([("wide", 0, 10), ("first", 0, 2), ("middle", 4, 6), ("end", 6, 8), ("past", 10, 20)]);
        var found = new List<string>();
        index.Visit(2, 6, found.Add);
        Assert.Equal(new[] { "wide", "middle" }, found);
        found.Clear();
        index.Visit(6, 10, found.Add);
        Assert.Equal(new[] { "wide", "end" }, found);
    }

    [Fact]
    public void Crossings_and_T_junctions_do_not_extend_double_strokes()
    {
        var side = new BorderSide(0.75, LineStyle: BorderLineStyle.Double);
        var cells = new[] { Cell(new(0, 0, 20, 20), new(Right: side, Bottom: side)), Cell(new(20, 0, 20, 20), new(Left: side, Bottom: side)), Cell(new(0, 20, 20, 20), new(Right: side, Top: side)) };
        var lines = BorderConflictResolver.Generate(1, cells).Cast<DrawLineCommand>().ToArray();
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Equal(2, BorderStrokeGeometry.GetStrokes(line.Style, line.X1, line.Y1, line.X2, line.Y2).Count));
        Assert.All(lines, line => Assert.Equal(40, Math.Abs(line.X2 - line.X1) + Math.Abs(line.Y2 - line.Y1)));
    }

    [Fact]
    public async Task Saved_border_ids_use_the_same_Core_page_resolver()
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SampleInputs", "BorderResolution", "border-conflicts.xlsx"));
        using var prepared = await Input.WorkbookInputPreparer.ReadAsync(input, new(), default);
        using var font = new Fonts.SingleFontContext(TestSupport.JapaneseFont);
        var sheet = new Excel.CoreExcelReader(font).Read(prepared, null, ["Double"]).Sheets.Single();
        var measurer = new NoTextMeasurer();
        var plan = Assert.Single(new ReportLayoutEngine(measurer).Plan(sheet));
        var page = plan.Build(0, 1, 1, measurer);
        var commands = new DrawCommandGeneratorPass().GeneratePage(page, null);
        Assert.Equal(3, commands.Count);
        Assert.All(commands, command => Assert.Equal(BorderLineStyle.Double, Assert.IsType<DrawLineCommand>(command).Style.LineStyle));
    }

    private static RenderCell Cell(ReportRect bounds, BorderStyle border) => new(new(null, CellStyle.Default with { Border = border }), bounds);

    private sealed class NoTextMeasurer : Abstractions.ITextMeasurer
    {
        public Abstractions.TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap) => throw new InvalidOperationException("Border-only fixture must not measure text.");
    }
}
