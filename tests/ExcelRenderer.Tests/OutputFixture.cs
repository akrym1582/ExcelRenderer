using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace ExcelRenderer.Tests;

/// <summary>固定書体・二行の文字レイアウト・ページ設定を共有して出力座標を検証します。</summary>
internal static class OutputFixture
{
    /// <summary>出力座標の検証に使う幅 120・高さ 80 ポイントのページ設定を保持します。</summary>
    internal static readonly PageSettings Page = new(120, 80);

    /// <summary>同梱の Noto Sans JP データと固定識別子を持つ標準書体を返します。</summary>
    internal static ResolvedFont Face => new("Noto Sans JP", 400, false, string.Empty)
    {
        FaceId = "output-fixture-noto",
        FontData = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf")),
    };

    /// <summary>同梱の日本語または等幅フォントを、字形の参照描画用に開きます。</summary>
    /// <param name="mono">参照描画に等幅フォントを使うかどうか。</param>
    internal static SKTypeface Typeface(bool mono = false) => SKTypeface.FromFile(Path.Combine(
        AppContext.BaseDirectory, mono ? "Fonts/NotoSansMono-Regular.ttf" : "NotoSansJP-Regular.ttf"));

    /// <summary>実効サイズ 9 ポイント、二行のベースラインと異なる文字オフセットを持つ描画命令を作成します。</summary>
    /// <param name="runs">確定済みレイアウトに文字ランを設定するかどうか。</param>
    /// <param name="underline">文字の下線を有効にするかどうか。</param>
    /// <param name="horizontal">文字の水平配置。</param>
    /// <param name="vertical">文字の垂直配置。</param>
    internal static DrawTextCommand Artificial(bool runs = true, bool underline = false,
        HorizontalAlignment horizontal = HorizontalAlignment.Left, VerticalAlignment vertical = VerticalAlignment.Top)
    {
        var face = Face;
        return new(1, new(10, 20, 100, 50), "ABC", CellStyle.Default with
        {
            Font = new("Noto Sans JP", 12, Underline: underline),
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
        })
        {
            TextLayout = new(new(40, 30),
            [
                new("AB", 40, 10, 3, runs ? [new(new("A", face), 0, 5), new(new("B", face), 25, 5)] : [], false),
                new("C", 20, 20, 17, runs ? [new(new("C", face), 7, 5)] : [], false),
            ]) { EffectiveFontSize = 9 },
        };
    }

    /// <summary>文字描画命令を固定ページに描画し、保存済み PDF のバイト列を返します。</summary>
    /// <param name="command">実行するコマンド、または描画対象の文字命令。</param>
    internal static byte[] Pdf(DrawTextCommand command)
    {
        using var output = new MemoryStream();
        new PdfSharpRenderer().Render([command], Page, output);
        return output.ToArray();
    }

    /// <summary>保存済み PDF の先頭ページを読み、演算子と引数を文字列として返します。</summary>
    /// <param name="bytes">解析対象の PDF データ。</param>
    internal static string Content(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        return string.Join("\n", ContentReader.ReadContent(document.Pages[0]).Cast<global::PdfSharp.Pdf.Content.Objects.COperator>().Select(op => string.Join(" ", op.Operands.Cast<global::PdfSharp.Pdf.Content.Objects.CObject>().Select(x => x.ToString())) + " " + op.OpCode.Name));
    }

    /// <summary>すべての要求を固定の Noto Sans JP 書体へ解決する、出力比較用のフォント管理です。</summary>
    internal sealed class FixedManager : IFontManager
    {
        /// <summary>書体要求に対して、このテストで設定した固定書体を返します。</summary>
        /// <param name="request">解決を要求するフォントファミリと書体設定（固定書体のテスト実装では選択に使用しません）。</param>
        public ResolvedFont Resolve(FontRequest request) => Face;

        /// <summary>入力文字列全体を固定の Noto Sans JP 書体に割り当て、一つの文字ランとして返します。</summary>
        /// <param name="text">計測・描画または解析の対象となる文字列。</param>
        /// <param name="request">解決を要求するフォントファミリと書体設定（固定書体のテスト実装では選択に使用しません）。</param>
        public IReadOnlyList<TextRun> ResolveTextRuns(string text, FontRequest request) => [new(text, Face)];
    }
}
