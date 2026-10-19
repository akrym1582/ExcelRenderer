# マネージド TrueType PDF フォント

Core の `Fonts/TrueType` に BCL のみの C# subset・TTC/OTC face 抽出を実装しています。
本体・Core・CLI で共有します。描画・PDF 生成には引き続き SkiaSharp 4.151.0 と
native assets が必要です。通常 restore/build/test/pack で独自 adapter・CMake/C++・
HarfBuzz ソース取得は不要です。

| 選択 face | PDF の扱い |
| --- | --- |
| glyf/loca を持つ非可変 TrueType（sfnt 0x00010000） | C# subset。元の GID を維持 |
| プロポーショナル・等幅、別 face の Regular/Bold/Italic | 同じ固定 TrueType 経路 |
| TTC v1/v2・OTC 内の固定 TrueType | 選択 face を抽出して subset |
| 既存の非可変 COLR/CPAL v0/v1、CBDT/CBLC、sbix | 元の正規化 face を既存 Skia カラー経路へ |
| CFF/CFF2・OTTO・可変 | カラー table があっても PDF は拒否 |
| WOFF/WOFF2・Type1・旧 true/typ1 | 対象外 |
| 構造破損・範囲外・循環参照 | table と理由を示して拒否 |

Noto Color Emoji を維持します。全 bitmap/SVG 方式や未対応 emoji sequence の新規保証は
行いません。カラーには通常輪郭 subset の削減率を適用しません。色 layer・paint graph・
bitmap strike は削らず Skia へ渡します。Restricted embedding は両経路で拒否し、
no-subsetting・bitmap-only に反する輪郭 subset を生成しません。

PDF で実際に使用する face を検査します。未使用の対象外フォントや除外ページだけの
フォントを理由に失敗しません。PNG/SVG/Markdown に PDF 専用制限を適用しません。
本体の日本語・IVS の選択 GID と Unicode/cluster を維持します。Core は単一 regular font と
IVS の基底文字化を維持します。IVS の cmap format 14・STAT 単独は可変判定しません。

## face index

0 始まりで、単体フォントは 0 のみです。既存の record 位置引数・Register メソッドは維持します。

```csharp
var registration = new ExcelRenderer.Fonts.FontRegistration("My Family", path, path)
{
    RegularCollectionIndex = 0,
    BoldCollectionIndex = 1,
    // ItalicCollectionIndex / BoldItalicCollectionIndex も指定可能
};
var fonts = new ExcelRenderer.Fonts.FontOptions { Registrations = new[] { registration } };
// manager.Register(registration) も利用可能
var core = new ExcelRenderer.Core.PdfExportOptions
{
    FontFilePath = path,
    FontCollectionIndex = 1,
};
var memory = new ExcelRenderer.Fonts.ResolvedFont("My Family", 700, false, "")
{
    FontData = System.IO.File.ReadAllBytes(path),
    FontCollectionIndex = 1,
};
```

`FontOptions.FontFiles`・追加 directory・システム走査は、各 face の実際の family/weight/italic
で候補を登録します。既存優先順位に続く path/index 順で決定します。metadata 走査で全 face の
table をコピーせず、選択時に遅延正規化します。snapshot は path/length/mtime/index を区別し、
コンテナー bytes を共有します。subset 共有は正規化後 bytes の hash に基づきます。

直接ファイルを1つだけ指定する場合、`FontOptions.FontFileCollectionIndex = 1` で選択できます。
未指定（null）は全 face の列挙です。0個・複数ファイルとの index 指定はエラーです。

```bash
excelrenderer pdf report.xlsx -o report.pdf \
  --font-file /fonts/family.ttc --font-face-index 1 \
  --fallback-font "My Family" --no-system-fonts
excelrenderer render report.xlsx --format pdf -o report.pdf \
  --font-file /fonts/family.ttc --font-face-index 1 --fallback-font "My Family"
```

CLI は `--font-file` が1つの場合だけ `--font-face-index` を受け付けます。
family の選択規則は維持するので、選ぶ family を要求または fallback へ含めてください。
負数・範囲外・単体への非ゼロ index は診断付きで拒否します。

## 内部仕様

元の不変 face で文字選択・IVS・計測・改行・ページ割りを確定し、subset は PDF 描画にだけ
使用します。GID 0 と合成 glyph の推移閉包を保持します。反復 DFS で循環を検出し、共有成分は
許容します。使用 glyph の輪郭・命令・成分 GID は維持し、未使用の輪郭を空にします。
maxp.numGlyphs と metrics は維持します。long loca・glyph 2byte/table 4byte 境界・checksum・
head の bbox/調整値を生成し、時刻は変更しません。

同梱 Noto Sans JP・IPAmj は long loca に奇数 offset を含むため、計画の一律偶数条件は
既存フォント維持と両立しません。long 入力は範囲・単調性を検査して受け入れ、出力はゼロ padding
で偶数境界へ揃えます。short loca は元々2byte単位です。この差異は実ファイルで確認しました。

内部 subset の保持 table は head/maxp/hhea/hmtx/cmap/name/OS/2/post、対になる vhea/vmtx、
存在する cvt/fpgm/prep/gasp です。layout table・DSIG・その他は除去します。
cmap は IVS を含め維持するため未使用文字が空の GID へ対応します。内部 subset から
新しい文字を解決・再 shaping しないでください。インストール用フォントの公開 API はありません。

TTC 抽出は別処理で、DSIG 以外の全 table（カラー・layout を含む）を維持します。
offset はコンテナー先頭基準とし、face 間の正当な table 共有を許容します。
選択・計測・描画・subset は同じ正規化 bytes を使います。共通抽出に PDF 専用拒否を混ぜません。
未知 table は不透明な payload として保持し、内部にコンテナー絶対 offset を持つ特殊技術まで
新規対応するものではありません。

subset 失敗時の全埋込・別 font/engine・ページ画像化への退避はありません。
fullEmbed は内部比較専用です。準備失敗時に完成 PDF を commit しません。
Core の既存 InvalidDataException、本体の conversion 診断、明示 FontFiles の ArgumentException を
維持し、理由に形式/table を含めます。キャンセルは OperationCanceledException を維持します。

検証結果は[今回の記録](validation/managed-truetype-subset/README.md)、再実行コマンドは
[英語版](managed-truetype-subset.md#verification)を参照してください。
独立 reader・fontTools・fixture 生成 Python は検証専用です。
