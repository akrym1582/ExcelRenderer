using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;

namespace ExcelRenderer.TestFixtures;

internal abstract class CObject;
internal sealed class CInteger(int value) : CObject
{
    internal int Value => value;
    public override string ToString() => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
internal sealed class CReal(double value) : CObject
{
    internal double Value => value;
    public override string ToString() => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
internal sealed class CString(string value) : CObject
{
    internal string Value => value;
    public override string ToString() => "<" + Convert.ToHexString(Encoding.Latin1.GetBytes(value)) + ">";
}
internal sealed class CName(string name) : CObject
{
    internal string Name => name;
    public override string ToString() => name;
}
internal class CSequence : List<CObject>
{
    public override string ToString() => string.Join("\n", this.Select(item => item.ToString()));
}
internal sealed class CArray : CObject, IReadOnlyList<CObject>
{
    private readonly CObject[] items;
    internal CArray(IEnumerable<CObject> items) => this.items = items.ToArray();
    public int Count => items.Length;
    public CObject this[int index] => items[index];
    public IEnumerator<CObject> GetEnumerator() => ((IEnumerable<CObject>)items).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public override string ToString() => "[" + string.Join(" ", items.Select(item => item.ToString())) + "]";
}
internal sealed class COperator(string name, IEnumerable<CObject> operands) : CObject
{
    internal CName OpCode { get; } = new(name);
    internal IReadOnlyList<CObject> Operands { get; } = operands.ToArray();
    public override string ToString() => string.Join(" ", Operands) + " " + name;
}
internal sealed class CToken(string value) : CObject
{
    public override string ToString() => value;
}
internal static class PdfTestContent
{
    internal static CSequence ReadContent(PdfTestPage page)
    {
        using var content = new MemoryStream();
        foreach (var operation in page.Operations)
        {
            operation.Write(content);
            content.WriteByte(10);
        }
        return Read(content.ToArray());
    }

    internal static CSequence Read(byte[] bytes)
    {
        var scanner = new CoreTokenScanner(new MemoryInputBytes(bytes), false, new StackDepthGuard(64));
        var result = new CSequence();
        var operands = new List<CObject>();
        while (scanner.MoveNext())
        {
            var token = scanner.CurrentToken;
            if (token is OperatorToken op)
            {
                result.Add(new COperator(op.Data, operands));
                operands.Clear();
            }
            else if (token is not CommentToken)
            {
                operands.Add(Wrap(token));
            }
        }
        if (operands.Count != 0) throw new InvalidDataException("Unconsumed PDF operands");
        return result;
    }

    private static CObject Wrap(IToken token) => token switch
    {
        NumericToken number when number.Double == Math.Truncate(number.Double) && number.Double >= int.MinValue && number.Double <= int.MaxValue => new CInteger((int)number.Double),
        NumericToken number => new CReal(number.Double),
        NameToken name => new CName("/" + name.Data),
        HexToken hex => new CString(Encoding.Latin1.GetString(hex.Bytes.ToArray())),
        StringToken text => new CString(Encoding.Latin1.GetString(text.GetBytes())),
        ArrayToken array => new CArray(array.Data.Select(Wrap)),
        _ => new CToken(token.ToString() ?? string.Empty),
    };
}
