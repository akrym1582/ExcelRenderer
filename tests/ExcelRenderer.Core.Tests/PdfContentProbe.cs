using UglyToad.PdfPig.Core;

namespace ExcelRenderer.Core.Tests;

// PdfPig interprets transforms, glyph advances and paths independently of the writer.
// Only the coordinate conversion from PDF's bottom-left origin belongs here.
internal sealed class PdfContentProbe
{
    internal sealed record Point(double X, double Y);
    internal sealed record Text(Point Origin, double Size, string Value, string Font);
    internal sealed record Paint(string Operation, Point[] Points);

    internal List<Text> Texts { get; } = [];
    internal List<Paint> Paints { get; } = [];
    internal int ClipCount { get; private set; }

    internal static PdfContentProbe Read(byte[] bytes)
    {
        using var document = TestSupport.Open(bytes);
        var page = document.GetPage(1);
        var result = new PdfContentProbe();
        Point TopLeft(PdfPoint point) => new(point.X, page.Height - point.Y);
        foreach (var letter in page.Letters)
        {
            result.Texts.Add(new(TopLeft(letter.StartBaseLine), letter.PointSize, letter.Value, letter.FontName ?? string.Empty));
        }

        foreach (var path in page.Paths)
        {
            var points = path.SelectMany(subpath => subpath.Commands).SelectMany(command => command switch
            {
                PdfSubpath.Move move => new[] { move.Location },
                PdfSubpath.Line line => new[] { line.To },
                PdfSubpath.BezierCurve curve => new[] { curve.StartPoint, curve.EndPoint },
                _ => Array.Empty<PdfPoint>(),
            }).Select(TopLeft).ToArray();
            if (path.IsFilled) result.Paints.Add(new("f", points));
            if (path.IsStroked) result.Paints.Add(new("S", points));
        }

        result.ClipCount = page.Operations.Count(operation => operation.Operator is "W" or "W*");
        return result;
    }
}
