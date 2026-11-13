# Coreの明示的なフォント選択

[English](core-fonts.md)

Core APIは、従来の `PdfExportOptions.FontFilePath`・`FontCollectionIndex`、または新しい `Fonts` 設定を受け付けます。両方の併用は引数エラーです。従来の単一フォント指定の動作は維持します。

```csharp
var options = new ExcelRenderer.Core.PdfExportOptions
{
    Fonts = new()
    {
        Files = ["NotoSansJP-Regular.ttf", "NotoSerifJP-Regular.ttf"],
        FamilyAliases =
        {
            ["游ゴシック"] = "Noto Sans JP",
            ["游明朝"] = "Noto Serif JP",
        },
        SansSerifFallback = "Noto Sans JP",
        SerifFallback = "Noto Serif JP",
        DefaultFamily = "Noto Sans JP",
    },
};
await ExcelRenderer.Core.ExcelConverter.ConvertAsync(xlsx, pdf, options);
```

`Fonts` の型は `ExcelRenderer.Core.Fonts.FontOptions` です。固定TrueTypeのファイルを1つ以上渡します。コレクションは各ファイルのface 0を使います。既存のPDF対応形式の検証を全ファイルに適用します。変換ごとのsnapshotを計測・折返し・縮小・字形選択・PDF埋め込みで共有し、同じ物理フォントのPDFサブセットは共有します。Coreのフォントパッケージ依存は追加しません。

セルごとに、次の順で1つのフォントを選びます。

1. 要求されたフォント名（ローカライズされた名前を含む）を持つ指定済みファイル。
2. `FamilyAliases` による、元の名前から指定済みファミリーへの直接対応。
3. Excelの保存済み分類が `<family val="1"/>`（Roman）の場合は `SerifFallback`、`<family val="2"/>`（Swiss）の場合は `SansSerifFallback`。
4. `DefaultFamily`。省略時は先頭ファイル。

名前・対応表のキーは大文字小文字を区別しません。同じファミリーの候補が複数ならファイル順を使い、太字・斜体の書体選択や合成は追加しません。対応表の置換先と各fallback/defaultのファミリーは `Files` 内に必要です。存在しない名前はエラーにします。対応表の連鎖はたどりません。元の物理ファミリーが一致した場合は対応表より優先します。

系統はExcelの保存済み分類から取得し、名前から推測しません。分類がない場合、その他の分類、対応する系統別fallbackが未設定の場合は既定フォントを使います。ゴシック・明朝の分類が正しく保存されていないブックには `FamilyAliases` を指定してください。ヘッダー・フッターは既定の描画フォントを使い、既存の書式コード処理は変更しません。

Coreでは引き続き異体字セレクターを除去し、リッチテキストの部分的なフォント書式とセルの太字・斜体を無視します。セル全体を1つのフォントで描画するため、未収録文字を別フォントで補完しません。OSからの描画用フォント検索や文字単位の自動fallback、追加のシェーピング処理は行いません。

列幅の計測方針は独立しています。元のNormalファミリーが指定済みのいずれかのファイルに一致すれば、そのフォントを実測します。対応表・系統別の置換は元フォントの一致とは扱いません。元フォント不明時や `UseSourceFontForColumnWidths = false` の既定値は引き続き8px固定です。`ReadOptions.FallbackMaximumDigitWidthPixels96Dpi = null` でNormal用に選んだ描画用の代替フォントを実測でき、数値での指定も可能です。常時優先する `MaximumDigitWidthPixels96Dpi` も維持します。任意の元フォントファイル／OS検索は列幅の計測だけに使います。

## CLIの既定フォント

```bash
excelrenderer core input.xlsx -o output.pdf
excelrenderer core input.xlsx -o custom.pdf --font-file ./regular.ttf
```

`--font-file` を省略すると、Toolが既存のフォントパックから Noto Sans JP を取得し、一時ファイルのsnapshotをCoreへ渡します。一時ファイルは成功・失敗時とも削除します。この描画用の既定フォントをOSから検索しません。同梱フォントが利用できない場合はエラーになるため、`--font-file` を指定してください。非ゼロの `--font-collection-index` にはファイルの明示指定が必要です。CLIの描画フォントは1つで、上記の複数ファイル・対応表はAPIの設定です。Core APIでは引き続きフォントの明示指定が必要です。
