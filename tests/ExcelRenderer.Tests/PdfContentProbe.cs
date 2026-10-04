using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

namespace ExcelRenderer.Tests;

// Deliberately a strict probe for the operators emitted by these fixtures, not a general PDF renderer.
// Unsupported operators or sequential text requiring font-dependent advancement fail with diagnostics.
internal sealed class PdfContentProbe
{
    internal sealed record Point(double X, double Y);
    internal sealed record Text(Point Origin, double Size, string Encoded, string Font);
    internal sealed record Paint(string Operation, Point[] Points, Point[][] Clips);
    private sealed record State(Matrix Ctm, double FontSize, string Font, Point[][] Clips);
    private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        internal static Matrix Identity => new(1, 0, 0, 1, 0, 0);
        internal Point Apply(double x, double y) => new(A * x + C * y + E, B * x + D * y + F);
        internal Matrix Times(Matrix r) => new(
            A * r.A + C * r.B, B * r.A + D * r.B,
            A * r.C + C * r.D, B * r.C + D * r.D,
            A * r.E + C * r.F + E, B * r.E + D * r.F + F);
    }

    internal List<Point[]> AppliedClips { get; } = [];
    internal List<Text> Texts { get; } = [];
    internal List<Paint> Paints { get; } = [];

    internal static PdfContentProbe Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        var page = document.Pages[0];
        // Fail closed if a gs resource could modify geometry or text state (for example /Font).
        var resources = page.Elements.GetDictionary("/Resources");
        var states = resources?.Elements.GetDictionary("/ExtGState");
        if (states is not null)
        {
            foreach (var key in states.Elements.Keys)
            {
                var dictionary = states.Elements.GetDictionary(key) ?? throw new InvalidDataException($"Invalid ExtGState {key}");
                foreach (var entry in dictionary.Elements.Keys)
                {
                    if (entry is not ("/Type" or "/CA" or "/ca"))
                    {
                        throw new InvalidDataException($"Unsupported ExtGState {key} entry {entry}");
                    }
                }
            }
        }
        var result = new PdfContentProbe();
        result.Parse(ContentReader.ReadContent(page), page.Height.Point);
        return result;
    }

    private static double Number(CObject value) => value switch
    {
        CInteger integer => integer.Value,
        CReal real => real.Value,
        _ => throw new InvalidDataException($"Expected PDF number, got {value.GetType().Name}: {value}"),
    };

    private void Parse(CSequence sequence, double height)
    {
        var state = new State(Matrix.Identity, 0, string.Empty, []);
        var stack = new Stack<State>();
        var textMatrix = Matrix.Identity;
        var lineMatrix = Matrix.Identity;
        var path = new List<Point>();
        var inText = false;
        var pendingClip = false;
        var consumed = false;
        var leading = 0d;
        var rise = 0d;
        Point Normalize(double x, double y)
        {
            var p = state.Ctm.Apply(x, y);
            return new(p.X, height - p.Y);
        }
        void MoveText(double x, double y)
        {
            lineMatrix = lineMatrix.Times(new(1, 0, 0, 1, x, y));
            textMatrix = lineMatrix;
            consumed = false;
        }
        void Show(string encoded)
        {
            if (!inText || consumed || encoded.Length == 0)
            {
                throw new InvalidDataException("Unexpected text show: BT and explicit positioning are required for each fixture run.");
            }
            var origin = textMatrix.Apply(0, rise);
            var combined = state.Ctm.Times(textMatrix);
            Texts.Add(new(Normalize(origin.X, origin.Y), state.FontSize * Math.Sqrt(combined.C * combined.C + combined.D * combined.D), encoded, state.Font));
            consumed = true;
        }
        void EndPath(string operation)
        {
            if (pendingClip)
            {
                AppliedClips.Add(path.ToArray());
                state = state with { Clips = [.. state.Clips, path.ToArray()] };
                pendingClip = false;
            }
            if (operation != "n")
            {
                Paints.Add(new(operation, path.ToArray(), state.Clips));
            }
            path.Clear();
        }
        foreach (var item in sequence)
        {
            if (item is not COperator op)
            {
                throw new InvalidDataException($"Unexpected content item: {item.GetType().Name}");
            }
            var args = op.Operands;
            double N(int i) => Number(args[i]);
            switch (op.OpCode.Name)
            {
                case "q": stack.Push(state); break;
                case "Q": state = stack.Count > 0 ? stack.Pop() : throw new InvalidDataException("Unmatched Q"); break;
                case "cm": state = state with { Ctm = state.Ctm.Times(new(N(0), N(1), N(2), N(3), N(4), N(5))) }; break;
                case "BT": inText = true; consumed = false; textMatrix = lineMatrix = Matrix.Identity; break;
                case "ET": inText = false; break;
                case "Tf": state = state with { FontSize = N(1), Font = ((CName)args[0]).Name }; break;
                case "Tm": textMatrix = lineMatrix = new(N(0), N(1), N(2), N(3), N(4), N(5)); consumed = false; break;
                case "Td": MoveText(N(0), N(1)); break;
                case "TD": leading = -N(1); MoveText(N(0), N(1)); break;
                case "T*": MoveText(0, -leading); break;
                case "TL": leading = N(0); break;
                case "Ts": rise = N(0); break;
                case "Tj": Show(((CString)args[0]).Value); break;
                case "TJ":
                    var array = (CArray)args[0];
                    if (array.Count != 1 || array[0] is not CString str)
                    {
                        throw new InvalidDataException("TJ with font-dependent advances is unsupported by this fixture probe.");
                    }
                    Show(str.Value);
                    break;
                case "Tc": case "Tw": case "Tr":
                    if (N(0) != 0) { throw new InvalidDataException($"Unsupported text state {op.OpCode.Name} {N(0)}"); }
                    break;
                case "Tz":
                    if (N(0) != 100) { throw new InvalidDataException("Nondefault Tz is unsupported."); }
                    break;
                case "m": case "l": path.Add(Normalize(N(0), N(1))); break;
                case "c":
                    path.Add(Normalize(N(0), N(1))); path.Add(Normalize(N(2), N(3))); path.Add(Normalize(N(4), N(5)));
                    break;
                case "re":
                    path.Add(Normalize(N(0), N(1))); path.Add(Normalize(N(0) + N(2), N(1)));
                    path.Add(Normalize(N(0) + N(2), N(1) + N(3))); path.Add(Normalize(N(0), N(1) + N(3)));
                    break;
                case "h": break;
                case "W": case "W*": pendingClip = true; break;
                case "S": case "s": case "f": case "f*": case "B": case "B*": case "b": case "b*": case "n": EndPath(op.OpCode.Name); break;
                // These cannot change origins or create paint. gs resources were checked above for opacity-only entries.
                case "rg": case "RG": case "g": case "G": case "w": case "J": case "j": case "d": case "M": break;
                case "gs":
                    // Only the validated opacity-only resources are admitted.
                    break;
                default: throw new InvalidDataException($"Unsupported PDF operator {op.OpCode.Name}, operands: {string.Join(" ", args.Cast<CObject>())}");
            }
        }
        if (stack.Count != 0 || pendingClip || inText)
        {
            throw new InvalidDataException("Unbalanced graphics/text/clip state at end of content.");
        }
    }
}
