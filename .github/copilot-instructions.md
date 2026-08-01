# Copilot 向け指示

## プロジェクト概要

このリポジトリは、Excel ファイルを中間モデルに変換し、レイアウト計算と描画コマンド生成を経て PDF を出力する .NET 10 ライブラリです。実装の対象は `src/ExcelRenderer`、テストは `tests/ExcelRenderer.Tests` にあります。

処理の主な流れは次のとおりです。

1. `Excel/ExcelReader.cs` が ClosedXML から `Model` の中間モデルを作成する。
2. `Layout/ReportLayoutEngine` が各レイアウトパスを順番に実行して `RenderDocument` を作成する。
3. `Drawing/DrawCommandGeneratorPass` がレンダリング対象のセルと画像を描画コマンドへ変換する。
4. `PdfSharp/PdfSharpRenderer` が描画コマンドを PDFsharp で PDF に出力する。画像は SkiaSharp でデコードする。

## 実装方針

- 中間モデル、レイアウト、描画、PDF 出力の責務を分離し、層をまたぐ変更は必要最小限にする。
- 新しいレイアウト処理は `IReportLayoutPass` として追加し、`ReportLayoutEngine` の実行順序を明示する。現在の順序は正規化、印刷領域解決、非表示行列処理、列レイアウト、行レイアウト、テキスト計測、セル境界計算、ページ分割である。
- 座標とサイズは PDF のポイント単位として扱い、ページ余白はページ分割時に適用する。
- 結合セルは左上セルに `RowSpan` と `ColumnSpan` を保持する。結合範囲内の他セルを重複して描画しない。
- null 許容参照型を維持し、公開 API の変更は既存の呼び出し側への影響を確認する。
- 未実装の機能を実装済みとして扱わない。PNG/JPEG などの画像は読み込みと描画に対応するが、ページをまたぐ画像の分割描画には対応していない。

## コーディング規約

- 既存の C# のファイルスコープ名前空間、レコード型、暗黙的な型推論のスタイルに合わせる。
- 変更に直接関係しないリファクタリングや依存関係の追加を行わない。
- テストには xUnit を使用し、振る舞いを変更する場合は `tests/ExcelRenderer.Tests` に対応するテストを追加または更新する。
- ドキュメントとユーザー向けメッセージは日本語で記述する。

## 検証

変更後はリポジトリのルートで次を実行する。

```bash
dotnet test Excel2Pdf.slnx
```
