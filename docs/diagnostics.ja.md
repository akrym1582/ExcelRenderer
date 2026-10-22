# 診断と確実なPDF出力

[English](diagnostics.md) · [本体/Core比較](main-core-parity.ja.md) · [ファイルサンプル](../samples/ExcelRenderer.Sample/README.ja.md)

変換成功とExcelの完全再現は別です。診断がなくても、対象外機能の再現保証にはなりません。
分岐には `Code` を使い、翻訳・変更される `Message` の文字列一致に依存しないでください。
Messageや例外にはブックの情報、数式、パス、画像bytesが含まれる場合があります。
そのままHTTPエラーやログへ送らず、必要なコード・位置だけを扱います。

本体の結果は `CompletionStatus`、Pages、Artifacts、Diagnosticsを持ち、通常成功は `Completed`。
診断ポリシーで失敗すると `ConversionException`（診断と完了済み生成物を保持）が発生します。
多くの実行時エラーはこの例外に包まれますが、引数エラー・キャンセルはそのまま伝播します。
一部bytesや完了済み生成物があっても、例外になった変換を成功ファイルとして返してはいけません。

本体診断には `Code / Severity / Stage / Message / SheetName / CellRange / ObjectId /
SourcePageNumber / OccurrenceCount / UnicodeSequence` があります。位置はnullの場合があり、
ページ番号は元ページを指します。Coreは `PageCount` と診断を返し、診断は
`Code / Message / SheetName / PageNumber` だけです。SeverityやOccurrenceCount、
本体と同じ診断ポリシーAPIはありません。

| 本体DiagnosticOptions | 既定値と効果 |
| --- | --- |
| EnableLayoutWarnings | false。選択出力ページの確定レイアウトで文字切れ・小文字を通知 |
| MinimumReadableFontSize | 6pt。正の有限値。倍率・ShrinkToFit後の出力文字サイズ |
| DetectUnsupportedFeatures | false。追加の未対応機能を検出。全省略の検出保証ではない |
| StrictMode | false。Info以外で失敗 |
| TreatAsErrors | 空。指定コードで失敗。Infoも対象 |
| SuppressedCodes | 空。結果から除外。失敗判定は取り消さない |
| MaxDiagnostics | 100。正の値。保持する一意な診断数。超過集約通知は別に付加 |

失敗判定は抑制・保持上限より先です。Errorは常に失敗、StrictModeはWarningも失敗、
TreatAsErrorsはInfoも失敗にします。同一識別子（コード・シート・セル範囲・object・元ページ・
Unicode列）は発生回数を合算。上限到達後も保持済み識別子は合算し、新規識別子は破棄件数へ加算、
末尾に `DiagnosticsTruncated` を付けます。新規識別子では容量確認が抑制より先なので、
上限到達後の抑制対象も破棄件数へ入る場合があります。集約通知は結果取得時に生成され、
新しい失敗ポリシーイベントにはなりません。抑制で失敗を許可することはできません。

セル単位の読取診断、レイアウト警告、範囲付き未対応機能は出力シート・範囲・ページに応じて
絞られます。シート全体/読取全体の診断はページなしで残る場合があります。
選択はすべての診断を一括抑制する仕組みではありません。可能な診断はsinkを開く前のpreflightで
判定しますが、描画や出力の遅い失敗では途中bytesが残ります。
根拠は [DiagnosticPolicyTests](../tests/ExcelRenderer.Tests/DiagnosticPolicyTests.cs)、
[LayoutDiagnosticTests](../tests/ExcelRenderer.Tests/LayoutDiagnosticTests.cs)、
[OpenXmlEndToEndTests](../tests/ExcelRenderer.Tests/OpenXmlEndToEndTests.cs)、
[ImageReviewTests](../tests/ExcelRenderer.Tests/ImageReviewTests.cs)です。

| コード | 本体 / Core | 意味・出力への影響 | 対応 / 関連テスト |
| --- | --- | --- | --- |
| MaximumDigitWidthFallback | 両方 | 元Normalの寸法なし、7px互換MDWで列幅・改ページが変わる可能性 | 本体なら元フォント寸法を指定。Normal情報維持。ColumnWidthMetricsTests / MainCoreParityTests |
| MaximumDigitWidthResolved | 本体Info / Core通知なし | 解決・上書きしたMDWの記録 | 寸法の出所確認。ColumnWidthMetricsTests |
| FormulaCachedValueMissing | 両方 | 保存結果なし、保存値モードで空文字 | 計算後保存、または本体の限定再計算を明示選択。FormulaDisplayTests / MainCoreParityTests |
| ImageDecodeFailed | 両方 | 完全decodeに失敗し画像省略 | 画像を交換。本体ImageReviewTests / Core ImagePayloadTests・PageAndImageTests |
| MissingIvsGlyph | 本体 / Core通知なし | 指定した異体字の字形なし | 対応フォントまたは基底文字置換を許可。ShapeAndFontTests |
| MissingPrivateUseGlyph | 本体 / Core通知なし | 私用領域の字形なし | 本来のフォントを指定。ShapeAndFontTests |
| CellTextClipped | 本体opt-in / Core通知なし | 確定文字がセルclipを超える | セル拡大・折返し・倍率見直し。LayoutDiagnosticTests |
| CellTextTooSmall | 本体opt-in / Core通知なし | 出力文字が閾値未満 | 縮小率・配置の見直し。LayoutDiagnosticTests |
| UnsupportedShape / UnsupportedDrawingObject | 本体検出 / Core通知なし・契約上省略 | 未対応描画を省略 | 代替表現・目視確認。ShapeAndFontTests / UnsupportedChartTests |
| UnsupportedRichTextFormatting / RichTextLayoutFallback | 本体検出 / Core通知なし・セル書式に統合 | 部分装飾や指定計測サービスで部分書式を保持できない | フォント・計測サービス確認。RichTextFallbackTests / RichTextAcceptanceTests |
| ConditionalFormattingLimitExceeded | 本体 / Core対象外 | ルール/母集団の処理上限。影響範囲は通常書式へ | sqref・ルール・データを簡素化。ConditionalFormattingTests.LimitTargets / Phase2 |
| DiagnosticsTruncated | 両方 | 保持上限を超える診断あり | 診断一覧を完全とみなさない。本体は上限調整可。DiagnosticPolicyTests |

通常の欠字やフォントfamily代替すべてに汎用警告が出るわけではありません。Coreの図形、リンク、
部分書式、回転、条件付き書式の意図した省略も全件通知しません。必要な文字の収録と機能範囲は
別途確認してください。Core内部の上限は一意100件と集約通知で、網羅的監査APIではありません。

拒否する診断コードは業務側で明示し、Codeで分岐してください。本体はDiagnosticOptionsで
失敗にでき、Coreは結果のDiagnosticsを呼出側で判定します。公開前に診断を確認するなら、
最終PDFを一時ファイルへ変換し、結果を確認してから公開または削除します。追加ディスクI/Oと
配信までの待ち時間が発生し、ライブラリ内部の中間スプールとは別物です。
ファイルサンプルは変換完了後に公開して診断を表示します。業務固有の拒否ポリシーは実装しません。

| 出力方法 | 用途 | 診断の扱い |
| --- | --- | --- |
| 呼出側Streamへ直接出力 | 警告付き出力を許容 | 診断は変換後に取得。送信済みbytesは撤回不可 |
| 診断確認後に公開 | 特定コードで公開を止める | 最終PDF一時ファイルの結果を確認し、公開または削除 |
| ファイル変換サンプル | 失敗時に旧成果物を保持 | 一時ファイルを閉じてから目的ファイルを置換 |

独自アプリでHTTP連携する場合、Response.Bodyを閉じずキャンセルを伝達します。
応答開始後は途中PDFへJSONを付加せず、ホストの例外処理へ委ねます。利用者入力とサーバー側の
フォント設定不備を区別してください。Coreの不正フォントもInvalidDataExceptionになるため、
この例外を一括して利用者入力エラーへ変換してはいけません。キャンセルも変換失敗と別扱いです。
CPUの読取・配置・native描画は同期のままで、最終送信の非同期化は即時中断の保証ではありません。

両APIの入力既定値は最大128MiB、メモリ閾値16MiB、入力一時ファイル無効、ZIP 10,000エントリ、
宣言された展開後合計512MiB。業務に合わせて設定してください。プロセスメモリ全体の上限ではありません。
本体はInputとPDF Buffering、CoreはReader Inputを持ち、Coreの内部PDFスプールは8MiB後に一時ファイル。
フォント・画像・レイアウトには別途メモリが必要です。一時領域の容量・書込権限・アクセス制御を整えます。

ライブラリは呼出側Streamを閉じません。本体SingleStreamOutputSinkは完了時flush、Coreは出力を
flushしません。RequestAborted/CancellationTokenを変換とコピーへ伝達します。
[本体ファイル例](../samples/ExcelRenderer.Sample/PdfFileExample.cs)と
[Coreファイル例](../samples/ExcelRenderer.Core.Sample/Program.cs)で所有権、ページ数、診断、キャンセルを確認できます。
