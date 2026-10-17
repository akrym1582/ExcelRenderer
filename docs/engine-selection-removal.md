# Removing workbook engine selection / エンジン選択の廃止

The main library, Core and Mapping always use the OpenXml reader/writer. The
former `Auto` choice followed exactly the same implementation, so engine selection
has been removed from the current source. Formula handling remains configurable:
`CachedValue` is the default; `Recalculate` opts into the limited built-in calculator.

This is a **source and binary breaking API removal** relative to 1.9.1. Remove the
settings below, rebuild dependent libraries/plugins and applications, and update
CLI invocations. No release version is assigned by this change. The earlier
[1.9.0 → 1.9.1 namespace migration](namespace-migration.md) is a separate change.

本体・Core の Reader と Mapping は常に OpenXml を使います。Auto は同じ実装を呼ぶだけ
だったため、エンジンの選択機能を廃止しました。数式の保存値利用／明示再計算は引き続き設定できます。
この変更は1.9.1に対するソース・バイナリ互換性を失うAPI削除です。設定を削除し、依存する
ライブラリ・プラグイン・アプリを再ビルドしてください。この変更ではバージョン番号を変更しません。

## Library migration / ライブラリの移行

| Removed surface | Action |
| --- | --- |
| `ExcelRenderer.Excel.ExcelReadOptions.Backend` / `.BackendSelected` | Delete the assignments; keep formula settings |
| `ExcelRenderer.Core.ExcelReadOptions.Backend` / `.BackendSelected` | Delete the assignments; keep formula settings |
| `ExcelRenderer.Mapping.MappingOptions.Backend` / `.BackendSelected` | Delete the assignments; keep mapping/calculation/input settings |
| `ExcelRenderer.Excel.ReaderBackend` / `ReaderBackendSelection` | Delete enum values, selection observers and stored engine configuration |
| `ExcelRenderer.Core.ReaderBackend` / `ReaderBackendSelection` | Delete enum values, selection observers and stored engine configuration |
| `ExcelRenderer.Mapping.MappingBackend` / `MappingBackendSelection` | Delete enum values, selection observers and stored engine configuration |

The enums are removed entirely, including `OpenXml`, `Auto` and the obsolete
`ClosedXml` identifier. There is no replacement engine enum or selection callback.
Remove these fields from any application-owned configuration models as well.
Existing input limits, cancellation, diagnostic reporting and staged output
protection continue to apply. Use conversion diagnostics to observe workbook
problems rather than an engine-selection notification.

```csharp
using ExcelRenderer.Excel;
using ExcelRenderer.Mapping;
using ExcelRenderer.OpenXml.Calculation;

// No Backend or BackendSelected is needed.
var document = new ExcelReader().Read(input, new ExcelReadOptions
{
    FormulaDisplayMode = FormulaDisplayMode.CachedValue,
});

ExcelTemplateMapper.Map(template, output, data, new MappingOptions
{
    FormulaCalculationMode = FormulaCalculationMode.Recalculate,
    Input = new() { AllowTemporaryFiles = true },
});
```

Core uses `ExcelRenderer.Core.ExcelReadOptions` through
`ExcelRenderer.Core.PdfExportOptions.ReadOptions` in the same way. The default
Mapping behavior still rejects value-producing formulas before evaluating data or
writing output; explicit recalculation enables the supported formula subset.

`Backend` と `BackendSelected` の代入、および列挙型・選択通知への参照を削除します。
利用側独自の設定ファイルや設定モデルに保存しているエンジン指定も整理してください。
入力上限・キャンセル・ワークブック診断・出力保護の設定はそのまま使えます。
数式の動作は[数式ガイド](formulas.ja.md)を参照してください。

## CLI migration / CLI の移行

Delete `--reader-backend openxml|auto`, `--mapping-backend openxml|auto` and the
selection-only `--verbose` flag. They are no longer recognized, including with an
explicit `closedxml` value, and fail argument parsing before opening input, data
or output. No replacement flag is needed.

```console
excelrenderer pdf input.xlsx -o result.pdf
excelrenderer xlsx template.xlsx --data data.json -o result.xlsx
excelrenderer render template.xlsx --data data.json --format svg -o output --formula-mode recalculate
```

The change applies to `pdf`, `image`, `svg`, `markdown`/`md`, `render` and `xlsx`.
`--data` still enables template mapping and remains required for `xlsx`.
`--formula-mode cached|recalculate` is retained. All CLI commands also keep their
existing nonzero exit status and error reporting when conversion fails.

3つの旧オプションをコマンドから削除してください。置き換え用の指定は不要です。
旧オプションは未認識の引数として入出力開始前に失敗します。`--data` と `--formula-mode` は
引き続き使用できます。数式の `Legacy` 指定は従来どおり拒否します。

## Development tools / 開発ツール

The integration performance worker now accepts:

```console
dotnet tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll integration-worker input.xlsx mapping 1000 captured.xlsx
```

Its arguments are input, operation, row count and optional mapping capture path;
the backend argument is removed. The benchmark/profiling scripts use that format. When comparing an old baseline
worker with `benchmark-mapping-structural-references.py`, add
`--baseline-legacy-arguments` to adapt only that historical binary's argument format.
`profile-mapping-allocations.py` no longer accepts `--backend`.
`compare-openxml-outputs.py` compares a separately built baseline CLI with the
current CLI; supply `--baseline-cli /path/to/ExcelRenderer.Tool.dll` rather than
selecting two engines in one build.

Historical validation reports and fixtures retain their recorded commands, API
names and measurements. They describe their original revisions; current usage is
in [OpenXml integration](openxml-integration.md) ([日本語](openxml-integration.ja.md)).

## Verification / 検証

The [retained results](validation/engine-selection-removal.json) record a Release
solution rebuild with zero warnings/errors, all **1,928 tests passing** (no skips),
four-package distribution validation and all nine isolated package consumers
(including the installed CLI). Test reductions remove duplicate OpenXml/Auto
cases and obsolete selector contracts; workbook snapshots, formula handling,
preflight data/output protection and all output formats remain covered.

Metadata comparison confirms that only the six listed exported selector types
were removed from main/Core/Mapping. A preserved pre-change CLI and the current
CLI produce identical PDF text/page geometry and Markdown/image artifacts for
the Japanese workbook and invoice template. The updated performance worker and
allocation profiler also run successfully without an engine argument.

CLI tests exercise every command, both Markdown names, missing input/data and a
pre-existing output sentinel: removed options fail parsing without touching the
sentinel, and ordinary commands map/render successfully without engine flags.
`--formula-mode` is still listed in help and tested for cached/recalculated output.

再ビルドは警告・エラー0件、全1,928テストと9通りのパッケージ利用検証が成功しました。
変更前後の代表サンプルの出力も一致しています。OpenXml／Auto の重複ケースと廃止した選択APIの
契約テストを整理し、描画結果・数式・データ評価前の検査・出力保護の検証を維持しています。
