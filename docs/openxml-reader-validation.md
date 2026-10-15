# Open XML Reader 実装・検証記録

## 実装の識別と基準

- ブランチ: `feature/openxml-reader`。
- `git fetch origin` 後の実装基準: `dbc9efa3b93a340b98dbdd7c5c8d7dacb4b37f6a`。計画書の調査基準と同一で差分なし。
- 検証・性能測定はコミット前の作業ツリーで実行した。その時点の機械可読レポートには `ImplementationBaseSha`、`WorkingTreeChanges=true` と、コード・テスト・検証ツールの `ValidationSourceSha256` を記録する。
- 環境: Debian GNU/Linux 13、.NET SDK 10.0.401、runtime .NET 10.0.12。比較・測定は en-US。数値書式テストには ja-JP も含む。

検証対象の source SHA-256: `D9A066C50CACA92BB8DD33DD9BA6A290C6E008B66A14D81A53A1EC90DCCA7FFF`。比較と性能 JSON の識別子は一致する。これは commit SHA ではなく、[検証ツール README](../tools/ExcelRenderer.OpenXml.Validation/README.md) に記載したファイル集合の SHA-256 である。

## 追加・変更ファイル

| 場所 | 内容 |
| --- | --- |
| `src/ExcelRenderer.OpenXml/` | 独立した netstandard2.1 Reader、独自不変モデル、診断コード、書式・色・印刷参照・ストリーム所有権処理、lockfile |
| `tests/ExcelRenderer.OpenXml.Tests/` | net10.0、SDK package + 手書き XML fixture、97 境界テスト、lockfile |
| `tools/ExcelRenderer.OpenXml.Validation/` | 比較・fixture 生成・独立プロセス性能測定、実行方法、lockfile |
| `Directory.Build.props`、`ExcelRenderer.slnx` | 新規3プロジェクト登録、新テストを既存と同じ analyzer 条件に追加 |
| `scripts/check-core-dependencies.py` | 独立 Reader の依存隔離と既存製品への非接続も検査 |
| `docs/openxml-reader.md`、`.ja.md`、README 日英 | API 契約、単位、対応範囲、メモリ制限、次工程の対応表 |
| 本書と `docs/validation/openxml/` | 実測・比較・チェック結果の保存 |

既存 Core/Main/Mapping のソース・公開 API・CLI オプション・既定 Reader は変更していない。製品プロジェクトに新しい ProjectReference はない。新ライブラリの製品依存は DocumentFormat.OpenXml 3.3.0、ExcelNumberFormat 1.1.0 のみで、StyleCop は既存共通設定のまま適用した。NuGet の公開は実施していない。

## 先行検証と回帰

着手時の既存 restore/rebuild は成功し、警告0・エラー0。既存657テストもすべて成功した。

| 最終チェック | 結果 |
| --- | --- |
| restore、`restore --locked-mode` | 成功、3つの新規 lockfile を保存 |
| solution Rebuild (`--no-restore --verbosity:minimal`) | 警告0、エラー0 |
| solution test (`--no-restore`) | **754成功、0失敗、0skip** |
| 内訳 | 既存 Main 466、Core 80、Mapping 49、Tool 62、新 Reader 97 |
| dependency check script | Core 所有権・依存方向、新 Reader 隔離とも成功 |
| Main / Tool の Release pack | 両方成功。ZIP 内の DLL 名と nuspec を検査し、新 Reader DLL/依存の混入なし |
| compare の失敗ゲート | 管理された不正 XLSX で exit 1 と ReadFailure 1件を確認 |

[チェック一覧 JSON](validation/openxml/checks.json)、[Rebuild 出力](validation/openxml/build.log)、[最終テスト出力](validation/openxml/tests.log)、[locked restore 出力](validation/openxml/locked-restore.log) を保存した。

新規97テストは、leave-open（失敗・Dispose・途中終了を含む）、キャンセル、列挙競合拒否、再列挙、選択外の不正 XML の遅延、chart sheet を含む元 index、座標推定・不整合・疎シート、型付き値、書式だけの空セル、SST/inline/rich/phonetic/escape、部分書式とセルごとの基底フォント、style 優先順位・xfId・apply 未指定/false/true、theme/indexed/RGB/tint、保存数式結果と shared master 後出し・array follower、印刷 union/引用/全行全列/fit 0/改ページ、独立上限と part 上限、Culture snapshot を確認する。

ExcelNumberFormat **固定1.1.0** の3引数 `Format(value, culture, isDate1904)` をコンパイル・実行した。1900系の59/60/61、1904系の0/1、0.5日、27時間・27.5時間、General/percent/currency/fraction/scientific を確認した。SDK **固定3.3.0** の `MaxCharactersInPart` は小さい上限で実際に失敗する。SDK が part reader の生成時点で上限を検出する場合もあるため、生成・Read・Load のいずれも `InvalidDataException` に統一している。

検証の途中で、実行中の回帰テストに対して Rebuild を重ねてしまい、一時削除された出力 fixture/font を参照したテストが失敗した。この実行は合格証拠に採用しない。ビルドとテストを分離した最終実行を以下の結果として採用する。

## 既存 fixture 比較

**15ファイル・2,396セルを読み取り、未説明差分0件、許容扱いの差分0件**。診断は UnresolvedColor 58件、FormulaCachedValueMissing 12件。未解決の色表現と保存数式結果の欠落を隠さず通知しており、描画再現済みとは扱わない。[全入力 hash・結果・診断 JSON](validation/openxml/compare.json) を保存した。

比較対象は raw XML に実在するセルのアドレス・件数・原値・型付き値・表示文字列、フォント/塗り/罫線/配置/色、結合、行列寸法、印刷範囲/scale/fit 幅/余白。ClosedXML の `CellsUsed` と XML の `c` は別の集合なので、件数を同一視しない。数式の presence/raw text/cache は元 XML を正解とし、ClosedXML の Value/GetFormattedString を数式セルで呼ばない。数式表示と group の期待値、繰り返し印刷範囲・詳細印刷情報・上限等は合成 fixture の独立テストで確認する。

結合は集合として比較する。列幅は保存値へ単位を合わせる。ClosedXML 0.105.1 の [LoadColumns](https://github.com/ClosedXML/ClosedXML/blob/0.105.1/ClosedXML/Excel/XLWorkbook_Load.cs) は保存幅から [ColumnWidthOffset=0.710625](https://github.com/ClosedXML/ClosedXML/blob/0.105.1/ClosedXML/Excel/XLConstants.cs) を引くため、検証側だけで加算して戻す。Reader 自体の列幅は元 XML のままである。この単位変換と結合の順序調整は、差分の一括許容ではない。

許容差分コードの実装は UnsupportedNumberFormat と、組み込み14の `mm-dd-yy` 対 ClosedXML の地域別表示に限定する。差分ごとに理由・field/address を出力する。今回の既存 fixture では、これらの許容を適用した差分はない。cache のない HYPERLINK 等を従来 Reader の復元表示と同一とは主張しない。

## Release 性能測定

生成処理を除外し、各 Reader/ケース/投影で warmup 1回と測定5回をそれぞれ新しいプロセスで実行した。値は中央値。新旧とも読取・Format・アドレス/表示文字列 checksum を行い、セル投影を全件保存しない。全 fixture・投影で件数と checksum の一致を要求する。parse-only は別結果であり、表示ありとの比較に混ぜない。

表示あり（読取＋Format＋checksum）:

| ケース | 新 Reader ms | ClosedXML ms | 割当 新 / 旧 MiB | Peak RSS 新 / 旧 MiB |
| --- | ---: | ---: | ---: | ---: |
| 普通の表 100,000セル | 1927.6 | 1345.4 | 302.6 / 122.9 | 70.6 / 97.6 |
| 共有文字列 100,000セル / SST 10 | 1889.7 | 1395.1 | 305.0 / 136.0 | 70.3 / 96.1 |
| ユニーク文字列 100,000セル / SST 100,000 | 2155.3 | 1964.1 | 429.1 / 191.0 | 99.9 / 127.3 |
| 書式付き空セル 100,000セル | 1831.7 | 1290.7 | 281.2 / 97.3 | 69.9 / 96.4 |
| 疎シート 2セル | 184.6 | 417.4 | 2.4 / 10.8 | 43.6 / 58.4 |
| 選択 10セル / 未選択 100,000セル | 183.4 | 1122.8 | 2.6 / 64.7 | 43.9 / 81.5 |

parse-only（表示なし、別投影）:

| ケース | 新 Reader ms | ClosedXML ms |
| --- | ---: | ---: |
| 普通の表 100,000セル | 1821.9 | 1205.5 |
| 共有文字列 100,000セル / SST 10 | 1891.7 | 1285.2 |
| ユニーク文字列 100,000セル / SST 100,000 | 2172.2 | 1915.2 |
| 書式付き空セル 100,000セル | 1778.1 | 1268.3 |
| 疎シート 2セル | 186.9 | 397.5 |
| 選択 10セル / 未選択 100,000セル | 180.7 | 1096.1 |

新 Reader の stage 別中央値（表示あり）:

| ケース | Open + style ms | Metadata pass ms | Enumeration + format ms | 内 SST load ms |
| --- | ---: | ---: | ---: | ---: |
| 普通の表 100,000セル | 140.5 | 1169.8 | 633.8 | 0.0 |
| 共有文字列 100,000セル / SST 10 | 145.9 | 1233.8 | 504.5 | 3.8 |
| ユニーク文字列 100,000セル / SST 100,000 | 142.4 | 1215.7 | 808.4 | 422.0 |
| 書式付き空セル 100,000セル | 141.8 | 1042.5 | 629.3 | 0.0 |
| 疎シート 2セル | 141.0 | 32.4 | 8.7 | 0.0 |
| 選択 10セル / 未選択 100,000セル | 141.4 | 32.3 | 8.3 | 0.0 |

[最終測定の全 sample・メトリクス JSON](validation/openxml/benchmark.json) を保存した。通常表の総一時割り当ては最適化前の約5,174 MiBから約302.6 MiBへ減少した。最終測定でも10万セルの密なケースは ClosedXML より遅く、選択・疎シートは速い。今回の全ケースでは新 Reader の Peak RSS が小さかったが、別の入力にも一般化する保証ではない。

elapsed は reader の Open から破棄までで、プロセス起動時間・入力 hash 計算・fixture 生成を含まない。allocation は総一時割り当てであり、保持メモリや RSS とは異なる。Peak RSS は runtime/JIT/依存ライブラリを含む独立プロセスのピークで、新旧を同一プロセスで測っていない。JSON には全 sample、Gen0/1/2、入力 SHA-256、実セル/SST/style 件数、Open+style/metadata/enumeration/SST 時間を保存した。SST 時間は enumeration 内に含まれるため合算時は二重計上しない。ClosedXML 内部の対応 stage は分離できないため Open と列挙に分けている。

初回の測定ではセルごとに SDK DOM → OuterXml → XElement.Parse と再解析しており、普通の10万セルで約5,174 MiBの一時割り当てが発生した。原因を確認し、SDK 要素の属性・子・leaf text から直接投影する方式へ修正した。コメントは表示文字列に含めない。[初回の診断用測定](validation/openxml/benchmark-before-projection.json) は別に残したが、変更途中の測定で source hash がないため最終受入証拠にはしない。上表は修正後の最終コードの結果である。全シート DOM や全セル辞書を保持する最適化は行っていない。

2回のシート走査、1セルごとの SDK DOM/独自データ生成、共有文字列表の保持にはコストがある。遅いケースを測定結果から除外せず、速度比を CI の固定合否にもしていない。未選択シートは本文を走査しないが、選択セルが SST を参照すれば共有表全体を保持する。上限は保持表の制限であってプロセス RSS の保証ではない。

## 既知制限・未検証事項・次工程

- 未検証の地域依存組み込み番号、locale 指定、DBNum、和暦は原文保持＋UnsupportedNumberFormat＋General。未知色は原文保持＋UnresolvedColor。
- ExcelNumberFormat の端数由来で、27.5時間の近似 double は `[h]:mm:ss` が `27:29:60` になる場合がある。数値をシフトしたり文字列置換で補正したりしない。
- 数式は保存結果のみ。共有式の相対参照展開、一般評価、再計算、高度な spill 追跡は未対応。原文・master・ref・cache を保持する。
- Drawing/画像、hyperlink、conditional formatting、table は存在情報のみ。PDF/PNG/SVG/Markdown の新 Reader 経由の出力、Excel 実機の見た目一致は今回未検証であり、受入証拠にしていない。
- 個々の XML entry を SDK が同期ロードしている間の即時キャンセル、任意の未検証 upload の安全性、プロセス RSS 上限は保証しない。
- 次工程では Core の入力検証を通した stream を渡す adapter、描画モデル・書式/run 合成・診断への変換、列幅のフォント計測と pt 変換、Full reader の ClosedXML 依存拡張点の整理が必要。移管後に旧 XML 読取を委譲/削除する。[日英 API 文書の対応表](openxml-reader.ja.md#次工程の責務と接続)を参照。
