# OpenXml 接続の実装・検証記録

## 基点

- 派生元: `origin/feature/openxml-reader`
- 着手時 fetch 成功後の SHA: `8f9f91989b753fad1a7afe38eb0860ca950c4539`
- 作業ブランチ: `feature/openxml-integration`
- 環境: Debian 13、.NET SDK 10.0.401 / runtime 10.0.12。
- 着手時の作業ツリーは clean。main やローカルキャッシュから代用していない。
- 基準 Rebuild: 警告0・エラー0。基準テスト: **778成功**（Core80 / Mapping49 / OpenXml121 / Main466 / Tool62）。

基準取得中にコンパイラサーバーへのサンドボックス内接続が失敗したため、以後の build は
`-m:1 -p:UseSharedCompilation=false` を併用する。ネットワークが必要な restore と testhost の
通信は許可された実行モードを使用する。ビルドと同じ出力先の test を重ねた中間実行は
受入証拠に採用せず、最後の分離実行のみを採用する。

## Reader 想定との対応

| 計画の想定 | 派生元の実装 | 今回の接続 |
| --- | --- | --- |
| 中立 source API | `IWorkbookDataReader` / `CellRecord` / `CellStyleInfo` が存在 | 継承して直接 `ReportCell` へ変換 |
| metadata を先に取得 | `ReadWorksheetInfo` と選択 index、元 sheet index を提供 | metadata、描画 metadata、画像識別子を先行取得 |
| 元値と cache | `CellValue.Type` と `FormulaInfo`、`FormatCell` が存在 | CachedValue 専用。literal link を警告前に復元 |
| 共有式 follower | 元の空文字・master/ref を保持、展開は未実装 | 別 tokenizer で A1 展開。未知文法は理由付き診断 |
| 画像 | 存在情報だけ | anchor / part URI / relationship / content type と reader 所有の image stream を追加 |
| 条件付き書式 | 存在フラグだけ | rule ごとの sqref を逐次取得し既存診断候補へ |
| Input/spool | Core 内の単独実装を要求 | OpenXml/Input へ移管し Core・本体は薄い adapter |
| Mapping | Reader に編集機能なし、既存言語は ClosedXML と結合 | snapshot・値・評価計画を分離し、型付き Writer を追加 |
| pack | `IsPackable=false` | OpenXml 1.8.0 を独立 pack、本体・Mapping の直接依存へ |

本体・Core の既定経路は旧 Reader のまま。旧 `FormulaCacheReader`、legacy graphic engine、
旧 Mapping の行操作 executor は互換経路で残す。新 Reader の hook は source snapshot を使い、
`XLWorkbook` / `IXLCell` / 旧 cache reader を呼ばない。通常のモデル組立・結合外周罫線・
layout・drawing/PDF 処理は Core の共有処理を使う。

## 比較方法と修正した差分

既存の tracked XLSX 15ファイルを CachedValue の両 Reader で比較する。
セル表示・式 metadata・書式・結合 span/外周罫線、列幅/行高/hidden、印刷範囲・タイトル・倍率・
改ページを比較する。XML にない結合先頭は source の有効な既定書式で補完する。

実出力では全 fixture を PNG と SVG へ変換する。PNG は出力 bytes をそのまま比較する。
Skia の SVG clip/image resource ID は process-global なので、SVG は id 宣言と対応する
`url(#id)` / `href=#id` だけを同じ順序で正規化し、その他の XML をすべて比較する。
最初に差分が出た5ファイルは、この native ID と参照だけの差だったことを個別確認した。
描画内容・順序・数値の差を包括許容しない。

接続中に検出し修正した不具合:

- tint=0 でも HSL 240段階へ丸めて色が1階調変わる問題。元 RGB を保持し、製品の theme tint 計算を共有。
- 結合先頭 XML 欠落時に Core の単一 font 既定が本体へ漏れる問題。
- 印刷範囲未指定を nullable の null ではなく `(0,0):(0,0)` として渡し、内容を除外する問題。
- 結合先頭補完時の列挙順。metadata の leader をセル走査中に補い、中間の全セル list を作らない。
- 空の `tableParts` を実 table と誤判定する問題。実 part / tablePart は引き続き拒否。
- ワイルドカードの値評価で配列 prefix の getter を再呼び出しする問題。
- active template より下の行高・hidden metadata を落とす問題。行配置 delta で source row を再生する。
- Markdown の座標計算が省略された行・列を0ptとしていた問題。既存の Core 共通 SheetGeometry adapter に委譲し、既定寸法・hidden を揃える。
- twoCellAnchor の画像寸法。描画には元の EMU を使う一方、Markdown の寸法と region 判定には旧経路と同じピクセル丸めを適用。
- CLI の command validator が未指定 backend の既定値を取得できない場合。未指定は従来の ClosedXml として検証。
- Reader Auto のページ指定と診断件数上限。候補の選択領域を共通 layout で検査し、理由は上限適用前に集約。

## 対応範囲

[日英の接続ガイド](openxml-integration.ja.md)を仕様とする。OpenXml Mapping の明示対応を
数式なしに限定し、数式や複雑な機能のある Auto テストを OpenXml 成功に数えない。
印刷範囲と展開領域が交差する Auto は `PrintAreaExpansionCompatibility` で旧経路を使う。
Reader の地域依存形式・未解決色・未知共有式には理由を残す。Recalculate は今回も拒否する。

最終 rebuild / 全 test / locked restore / pack / consumer / 性能の結果は、 `docs/validation/openxml-integration/` のログと JSON に保存する。


## 最終の機能・配布ゲート

- solution restore / locked restore 成功。
- solution Rebuild: **警告0・エラー0**。Release build も警告0・エラー0。
- 全テスト **865成功、失敗0、skip0**（Core80 / Mapping68 / OpenXml130 / Main514 / Tool73）。
- tracked XLSX 15件: モデル、PNG、SVG、Markdown と画像 artifacts を新旧 Reader で比較して一致。
- 同じ15件の PDF: `pdftotext -layout` の出力、ページ数、用紙サイズが一致。
- 数式なしの Mapping OpenXml → Reader OpenXml → PDF/PNG/SVG/Markdown は明示新経路で成功。
- 数式ありの Mapping Auto は `FormulasPresent` で旧経路を選択し、入力7/13で再計算 cache14/26と Markdown が変わることを確認。
- 依存方向・共有 Input・中立 hook・Mapping 言語・Markdown の共通座標委譲の静的チェック成功。
- OpenXml / main / Mapping / Tool のローカル pack 成功。`--no-build` pack は既存 Core 同梱 target のため `-p:BuildProjectReferences=false` を併用。

`Cells` は保持した ReportCell の個数であり、描画モデルを作らない Mapping 単体では0。
Mapping は評価計画・出力行の値を保持するが、元 worksheet のセル DOM を保持しない。
Writer は1行分の XML と spill 可能な行ストア、出力 ZIP stage を使う。


## 性能計測の条件

測定した C# 実装は `767f4e2f4754067332e9b20c6fef55a5f520bc50`。
Release、en-US、同じ NotoSansJP-Regular.ttf、独立プロセスで warmup 1回・測定5回の中央値を使う。
Reader 単体、PDF 描画全体、Mapping 単体、Mapping→PDF を分ける。
時間には各 backend の入力準備・Auto 能力検査を含め、入力データ配列の生成は含めない。
入力ファイルとフォント、Python runner の SHA-256 を JSON に記録する。

通常の32比較は1万・10万明細行の plain / nested / geometry-header を使う。
plain と nested は明細に書式付き空セルを持つ。geometry-header は結合したヘッダー、
展開する印刷範囲、繰り返しタイトルを追加する。Auto は後者を
`PrintAreaExpansionCompatibility` で旧経路へ送るため、新経路の成功として数えない。

生成器の worksheet 全体へのフォント指定は全16384列の style として保存される。
通常比較は ZIP の列定義を実際に使う2列または4列までに限定した `bounded/` 入力を使う。
実際のセル値・明示書式・行高・列幅・印刷設定は両 backend で同一。
全列 style 版の中間測定は通常比較に混ぜない。

行ごとに結合を複製する1万行の補足比較は
`benchmark-repeated-merges.json` に、旧経路・明示新経路・Auto の各6プロセスを保存する。
この補足は元の全列フォント定義を持つ入力であり、通常比較と直接速度比を作らない。
各行結合の10万行ファイルの生成も明示 OpenXml Writer で成功しているが、
これを旧経路との10万行性能比較として報告しない。

再現手順:

```sh
dotnet build ExcelRenderer.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll integration-generate artifacts/integration-fixtures/10000 10000
dotnet tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll integration-generate artifacts/integration-fixtures/100000 100000
python scripts/benchmark-openxml-integration.py --fixtures artifacts/integration-fixtures --output artifacts/integration-benchmark.json
python scripts/compare-openxml-outputs.py --work artifacts/backend-output-comparison --output artifacts/backend-output-comparison.json
python scripts/validate-openxml-packages.py --packages artifacts/integration-packages --work /tmp/excelrenderer-package-consumers
```

ローカル consumer は専用 NuGet package directory と package source mapping を使い、
OpenXml 単独、Mapping 単独、本体と同梱 Core を復元・build・実行した。
Tool もローカル NuGet から install し、shim 起動と明示 OpenXml の Markdown 出力を確認した。
SDK が wrapper 配下にある環境では、installed runtime の場所から `DOTNET_ROOT` を指定する。
本体・Mapping の nuspec は OpenXml 1.8.0 に依存し同 DLL を重複同梱せず、
Core DLL は本体に同梱、Tool は OpenXml/Core/Mapping を各1個含むことを検査した。
公開・タグ作成・push は行っていない。


## 性能結果と受入記録

通常32組＋補足3組、**210独立プロセス**（warmup35 / 測定175）を完了。
各 median を生データから再計算し、入力・runner の checksum、Reader の保持セル数・表示テキスト checksum、
backend ごとの XLWorkbook 生成回数を検査した。
[全結果の集計表](validation/openxml-integration/performance.md)と
[通常比較の生データ](validation/openxml-integration/benchmark.json)、
[補足比較](validation/openxml-integration/benchmark-repeated-merges.json)、
[検査結果](validation/openxml-integration/performance-assertions.json)を参照。

10万行の中央値（秒）:

| 処理 | ClosedXml | OpenXml | Auto |
| --- | ---: | ---: | ---: |
| Mapping plain | 3.63 | 4.01 | 4.43 |
| Mapping 結合ヘッダー・印刷 | 12.98 | 3.91 | 12.80（旧経路） |
| Reader 単体 | 6.87 | 4.90 | 5.87 |
| PDF 全体 | 11.88 | 10.08 | — |
| Mapping→PDF | 16.64 | 13.17 | — |

OpenXml はすべてのケースで速いわけではない。plain 10万行では累積 managed allocation が
ClosedXml 約515.5MiBに対して OpenXml 約1658.9MiBに増え、Peak RSS は約291.1MiBから
約271.7MiBへ下がった。結合ヘッダー・印刷では時間・割当量・Peak RSS が改善した。
割当量を保持メモリと取り違えず、速度比を CI 閾値にしない。

Reader は両サイズとも旧・新・Auto でセル数と表示テキスト checksum が一致。
10万行は **200,002 ReportCells**。明示新経路、能力が対応する Reader Auto / Mapping Auto は
全測定で **XLWorkbook 生成0**。Mapping→PDF の旧経路は2、旧 Reader は1。
印刷ケースの Auto は旧経路1であり、明示新経路の実績に含めていない。
今回の圧縮済み入力・ZIP stage は閾値内に収まり、実測 spool file bytes は0だった。
強制 spill と成功・失敗・キャンセル時の cleanup は機能テストで別に確認した。

[最終テストログ](validation/openxml-integration/tests.log)、
[Rebuild](validation/openxml-integration/rebuild.log)、
[出力比較](validation/openxml-integration/output-comparison.json)、
[package consumer](validation/openxml-integration/consumers.log)、
[package inventory](validation/openxml-integration/package-inventory.json)を保存した。
全列フォントの中間測定、同じ出力先の build/test を重ねた中間実行、
Auto で旧経路に fallback したケースを新経路の成功へ混ぜていない。

残る製品上の制約は接続ガイドに記載する。OpenXml Mapping は数式なしの保守的な編集に限定し、
複雑な part、数式、展開と交差するタイトルなどを拒否する。Auto は理由付きで旧経路を選ぶ。
Reader は CachedValue 専用で、地域依存形式・不明な共有式等は診断または Auto 旧経路となる。
特殊文字 inlineStr の ClosedXML 0.105.1 復号制約も日英ガイドに明記している。

consumer の stderr には sandbox の Fontconfig cache 書込不可メッセージが残る。
consumer build は警告0・エラー0、installed Tool は終了0と Markdown artifact の生成で確認した。


## plain 10万行の累積割当を追加調査

対象は `cc7043fcf1bbe8da1acf56c4798315be8dbea547` の製品コード。
計測用の別 worktree `/tmp/excel-allocation-investigation` にだけ区間計測と試験変更を加えた。
製品側の C# は変更していない。元の実装でも約1657MiBを再現した。
同一入力・Release・en-US・独立プロセス warmup1回＋測定5回で比較した。

**主因は `SheetSource.ReadRow` が同じ明細行の XML を10万回再解析することだった。**
[`ReadRow`](../src/ExcelRenderer.OpenXml/OpenXmlWorkbookWriter.Geometry.cs) は出力行ごとに
row store から byte array を作り、UTF-8文字列化し、`XElement.Parse` している。
今回のテンプレートは Header / 明細 / Footer の3行だが、出力時に100,002回これを繰り返す。
同じ約160バイトの行 XML に対し、Parse 区間だけで1回平均約12,349バイトを割り当てていた。
XML reader・解析バッファ等を毎回構築する処理が、反復行数に比例して積み上がる。
ここでいう Parse 区間は parser 内部と生成 XElement を合わせた値で、型別の配賦ではない。

計測版の区間中央値:

| 区間 | 累積 managed allocation (MiB) |
| --- | ---: |
| `XElement.Parse` | **1177.7** |
| row store の byte array / UTF-8文字列 | 47.3 |
| セル参照・値・spans の編集 | 105.8 |
| 行 XML 書出し | 44.6 |
| 完成 ZIP の Reader 再検証 | 175.6 |
| 共通評価計画 | 29.9 |
| 出力行・編集値への展開 | 21.8 |
| Writer 準備・配置 index | 45.5 |

総割当は全スレッドの `GC.GetTotalAllocatedBytes(true)`、区間は同期 Mapping 呼出し中の
`GC.GetAllocatedBytesForCurrentThread()` を使用した。表は子区間を抜き出しており、
生データの `writer.sheet` / `writer.readRow` とその子区間を二重合算しない。
独立した区間中央値・バックグラウンド割当・小さな準備処理のため、表の和は総量と一致しない。

反証実験として、直前の元行1件だけを保持し、編集前に `new XElement(prototype)` で複製した。
元 XML が64KiBを超える行は cache しない。全テンプレートの DOM を保持する方式にはしていない。

| 100,000明細行 | 総割当 MiB | Peak RSS MiB | 参考時間 秒 |
| --- | ---: | ---: | ---: |
| ClosedXml 再測定 | 512.6 | 301.2 | 3.60 |
| OpenXml 区間計測版 | 1660.0 | 270.8 | 3.94 |
| OpenXml 直前1行 cache 試験版 | **503.3** | 270.3 | 3.67 |

総割当は **69.7%減**、行復元区間は1225.0MiBから67.2MiBへ低下した。
Parse 区間は1177.7MiBから約0.035MiBへ下がり、代わりに行の複製が約67.1MiBとなった。
Peak RSS はほぼ同じであり、一時割当とGC負荷を大きく減らす改善である。
時間は区間計測を含む参考値で、製品版の正式な速度比較とは区別する。

生成した10万行 XLSX は **全10 ZIP part の展開後 bytes が完全一致**した。
比較対象は前回報告のとおり B列に書式付き空セルを持つ同じ bounded plain 入力。
複数行の明細や大きな行では単一行 cache の hit率が下がるため、一般化する場合は
総容量上限付きの小さな cache と、巨大行・非反復行の fallback を設計する必要がある。
再検証 Reader の175.6MiBは残る次の改善候補だが、今回そこは省略・変更していない。

再現用に [実行スクリプト](../scripts/profile-mapping-allocations.py)、
[区間計測 patch](validation/openxml-allocation-investigation/instrumentation-detailed.patch)、
[試験 cache patch](validation/openxml-allocation-investigation/cache-prototype.patch)、
[元経路の生データ](validation/openxml-allocation-investigation/baseline-detailed.json)、
[試験版の生データ](validation/openxml-allocation-investigation/cached-row.json)、
[ClosedXml 再測定](validation/openxml-allocation-investigation/closedxml.json)、
[全 part の比較結果](validation/openxml-allocation-investigation/output-equivalence.json)を保存した。
patch は **調査専用**であり、製品へ適用済みの修正ではない。

再現する場合は対象 commit の別 worktree に区間計測 patch を適用し、Performance project を
Release build して以下を実行する。baseline 採取後に cache patch を適用して再 build する。
再現には元の integration fixture を生成して bounded 入力を準備しておく。

```sh
python scripts/profile-mapping-allocations.py --worker /tmp/excel-allocation-investigation/tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll --input artifacts/integration-fixtures/bounded/100000/plain-100000-template.xlsx --output artifacts/allocation-baseline.json --capture artifacts/allocation-baseline.xlsx --variant baseline
```

試験版は solution Rebuild 警告0・エラー0、全865テスト成功（skip0）。
[試験版ログ](validation/openxml-allocation-investigation/prototype-tests.log)と
[調査の検査結果](validation/openxml-allocation-investigation/assertions.json)を保存した。
旧経路はこの plain 条件で直配置の `ApplyRegion` を使え、元行の値・書式を一度取り出して再利用する構造である。

保存した patch は空 context 行を含まない zero-context 形式で、適用時は
`git apply --unidiff-zero <patch>` を使う。cache patch は区間計測 patch の後に適用する。
両 patch の再構成と試験版ソースへの一致を確認した。
