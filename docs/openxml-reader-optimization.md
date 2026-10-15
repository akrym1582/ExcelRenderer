# Open XML Reader 追加最適化と接続境界

## 対象と変更

ドラフト PR #76 の初回実装 `cbf5257bba8382e61794ef8ac1a20710a913b2f6` を比較基準とした。Core・本体への接続は行っていない。既存の Core/Main/Mapping/Tool のソースと project reference は変更せず、共通の依存チェックも維持している。

初回計測では、普通の10万セルで metadata pass が約1.17秒、列挙＋表示が約0.63秒だった。メタデータと本文の両方で1セルの SDK DOM をロードして XElement に投影し、座標は正規表現・文字列置換・部分文字列で解析していた。改善は次に限定し、メタデータ走査を省いたり全セルをキャッシュしたりしていない。

- セルの座標・style・型・保存値を SDK の SAX イベントと `GetText()` から直接読み、セル DOM/XElement の生成を除去。数式だけは小さい要素をロードし、shared/array master の情報を維持する。
- SST/inline は本文と phonetic を逐次読み、部分書式 `rPr` だけをロードする。通常文字列では、run の List/LINQ 連結を避ける。共有表全体を保持する方式と各種上限は維持する。
- A1 は上限を確認しながら1回で解析し、正規表現・Replace・Substring を除去。通常文字列は `_x` がなければ escape 用 Regex.Replace を呼ばない。
- 表示診断の List・closure・ReadOnlyCollection は診断が発生するときだけ作る。同一 style が続く場合の列挙側の再解決を省く。General の表示は固定 ExcelNumberFormat を使い続け、独自の数値・日付整形に置き換えない。

書式付き空セル、XML 上の隣接する空要素、空キャッシュ/欠落/0/false、SDK escape の1回 decode、phonetic 除外と文字数加算、途中終了・キャンセル、shared master 後出し・array follower は従来の契約を維持する。表示なしの比較だけで高速化を主張しない。

## Core・本体向け API

| API | 将来の adapter での用途 |
| --- | --- |
| `IWorkbookDataReader` | 元データと保存値表示の消費境界。SDK 型・描画モデル・計測カウンターを公開契約に持ち込まない |
| `SelectedWorksheetIndices` | 元のブック順で選択 worksheet だけを取得。chart sheet は `Sheets` に残す |
| `ResolveStyle(int)` | 行・列・既定 style 0 を、偽のセルやランダムセル検索なしで解決 |
| `CellValue.Type` / `CellValueType` | 型安全な値分岐、General 配置の判断、変換 cache key。既存 `Kind` 文字列は維持 |
| `CellStyleInfo.FontProperties` / `WorkbookInfo.DefaultFontProperties` | 元のフォントを nullable な `FontInfo` として読む。名前・pt サイズ・bool・underline・scheme・色を保持 |
| `RichTextRunInfo.FontProperties` / `FontInfo.WithOverrides` | セルごとの基底 font に明示 run 属性だけを重ねる。false / underline none と null 継承を区別し、SST に合成結果を保存しない |

元の `ElementInfo` も残すため、typed font view に含めない charset/family/outline 等や、罫線・塗り・配置の未移管属性を失わない。FontInfo はフォント解決・描画結果を表す型ではなく、未指定値には null を残す。公開スナップショットの constructor は internal のままで、任意の外部 backend の構築 API まで約束するものではない。

次工程では Core 内の adapter が、Reader session と product policy ごとに `(StyleIndex, CellValue.Type, policy)` で変換書式を cache する。Core が入力検証/spool、列幅から pt への変換、フォント計測、結合の補完、モデル化と layout を所有し、本体は同じ adapter に拡張情報を付加する。表示文字列はここで正規化せず、Core の IVS 制限と本体の方針を adapter で分ける。Drawing/画像/link は prepared input の別 cursor から読み、元 sheet index/name/address で関連付ける。

診断は既存の集約層へ変換し、数式モードの経路選択は製品側の責務にする。本 Reader は CachedValue 専用のまま。既存の IXLCell/IXLWorksheet hook と公開 ExcelStyleConverter の変更は今回含めない。具体的な使用例と責務境界は[日本語 API 文書](openxml-reader.ja.md#core本体向けの接続境界未接続)と[英語 API 文書](openxml-reader.md#adapter-boundary-for-core-and-the-full-library-not-connected)に記載した。

## 検証

- `dotnet restore ExcelRenderer.slnx --locked-mode` 成功。
- solution Rebuild は警告0・エラー0。Release の検証ツール build も警告0・エラー0。
- 全回帰 **778件成功、失敗0、skip0**。内訳は Main 466、Core 80、Mapping 49、Tool 62、新 Reader 121。
- 新 Reader の追加24ケースで、interface 経由の選択・metadata/style ID 解決、型付き値と既存 Kind の一致、セルごとの部分 font 合成、Reader 破棄後の font snapshot、座標の不正/overflow 入力、空要素/保存結果/phonetic を含む SAX 読取と上限を確認。
- 既存 **15ファイル・2,396セル** の比較は未説明差分0、許容扱い差分0。UnresolvedColor 58件・FormulaCachedValueMissing 12件は以前と同じ診断として通知。
- 依存・所有権チェック成功。Core/Main/Mapping/Tool のソース・project reference の差分なし。今回既存製品の project/package 設定は変更していないため、初回記録の pack 検証を維持し、追加の DLL 同梱も行っていない。

[チェック一覧](validation/openxml-optimized/checks.json)、[Rebuild](validation/openxml-optimized/build.log)、[全テスト](validation/openxml-optimized/tests.log)、[locked restore](validation/openxml-optimized/locked-restore.log)、[fixture 比較](validation/openxml-optimized/compare.json) の結果を保存した。

検証・計測は commit 前の作業ツリーで行った。基準 commit は `cbf5257bba8382e61794ef8ac1a20710a913b2f6`、追加最適化の source SHA-256 は `204039243FA6EB5220966F8DDA29F4F8DCBD0BFA856C03802FDA87449729EF6D`。比較と性能の識別子は一致する。旧実装は clean worktree の source SHA-256 `D9A066C50CACA92BB8DD33DD9BA6A290C6E008B66A14D81A53A1EC90DCCA7FFF` で、初回測定のコード識別子とも一致する。hash の対象ファイル集合は[検証ツール README](../tools/ExcelRenderer.OpenXml.Validation/README.md)に記載している。

## 同一条件の再測定

元コミットを `/tmp` の detached worktree に展開し、その検証ツールを Release build した。新旧で同じ絶対パスの6 fixture を使用し、生成処理は測定に含めない。全回帰テスト完了後に旧実装、新実装の順で測定し、build/test/benchmark を同時実行しない。各実装の新 Reader と ClosedXML は、それぞれ別のプロセスで warmup 1回、測定5回。中央値と全 sample を保存する。

環境は Debian GNU/Linux 13、.NET SDK 10.0.401 / runtime 10.0.12、Culture en-US。起動・hash 計算は elapsed の外側、Reader の初回使用/JIT・Open・metadata・列挙・Format・破棄は内側。Peak RSS は runtime/JIT を含む独立プロセスのピークである。全ケースについて input SHA-256、セル件数、checksum が新旧および ClosedXML と一致することを確認する。

表示あり（読取＋Format＋checksum、ms）:

| ケース | 改善前 | 改善後 | 所要時間削減 | ClosedXML 前 / 後 |
| --- | ---: | ---: | ---: | ---: |
| 普通の表 100,000セル | 1931.9 | 1304.2 | 32.5% | 1334.2 / 1434.8 |
| 共有文字列 100,000セル / SST 10 | 1928.1 | 1148.6 | 40.4% | 1389.8 / 1309.3 |
| ユニーク文字列 100,000セル / SST 100,000 | 2206.8 | 1455.1 | 34.1% | 2026.0 / 1961.5 |
| 書式付き空セル 100,000セル | 1772.1 | 1129.4 | 36.3% | 1267.4 / 1281.4 |
| 疎シート 2セル | 222.0 | 195.9 | 11.8% | 425.7 / 399.8 |
| 選択 10セル / 未選択 100,000セル | 193.4 | 205.2 | -6.1% | 1404.6 / 1125.8 |

割り当て・Peak RSS（表示あり、MiB）:

| ケース | 割当 前 / 後 | 割当削減 | Peak RSS 前 / 後 |
| --- | ---: | ---: | ---: |
| 普通の表 100,000セル | 302.6 / 144.7 | 52.2% | 70.1 / 70.0 |
| 共有文字列 100,000セル / SST 10 | 305.1 / 128.6 | 57.8% | 69.9 / 69.6 |
| ユニーク文字列 100,000セル / SST 100,000 | 429.1 / 180.0 | 58.1% | 99.3 / 92.7 |
| 書式付き空セル 100,000セル | 281.2 / 117.2 | 58.3% | 69.3 / 69.1 |
| 疎シート 2セル | 2.4 / 2.4 | 0.3% | 43.2 / 43.6 |
| 選択 10セル / 未選択 100,000セル | 2.6 / 2.6 | 0.7% | 43.6 / 43.7 |

stage 別中央値（表示あり、ms。SST は列挙時間の内数）:

| ケース | Metadata 前 / 後 | 列挙＋表示 前 / 後 | 内 SST 前 / 後 |
| --- | ---: | ---: | ---: |
| 普通の表 100,000セル | 1163.0 / 631.3 | 619.7 / 509.7 | 0.0 / 0.0 |
| 共有文字列 100,000セル / SST 10 | 1227.1 / 686.0 | 566.3 / 318.5 | 4.1 / 2.8 |
| ユニーク文字列 100,000セル / SST 100,000 | 1208.1 / 681.6 | 837.1 / 626.4 | 445.1 / 321.7 |
| 書式付き空セル 100,000セル | 998.8 / 588.6 | 609.8 / 356.4 | 0.0 / 0.0 |
| 疎シート 2セル | 38.4 / 31.4 | 9.5 / 8.4 | 0.0 / 0.0 |
| 選択 10セル / 未選択 100,000セル | 32.8 / 33.3 | 9.2 / 9.0 | 0.0 / 0.0 |

parse-only（別投影、ms）:

| ケース | 改善前 | 改善後 | ClosedXML 改善後測定時 |
| --- | ---: | ---: | ---: |
| 普通の表 100,000セル | 1874.0 | 1179.2 | 1294.3 |
| 共有文字列 100,000セル / SST 10 | 1870.5 | 1240.9 | 1440.9 |
| ユニーク文字列 100,000セル / SST 100,000 | 2144.7 | 1464.0 | 1975.4 |
| 書式付き空セル 100,000セル | 1763.3 | 1136.3 | 1282.6 |
| 疎シート 2セル | 185.3 | 187.2 | 401.9 |
| 選択 10セル / 未選択 100,000セル | 206.8 | 187.4 | 1151.8 |

[改善前の全 sample](validation/openxml-optimized/benchmark-baseline.json) と[改善後の全 sample](validation/openxml-optimized/benchmark-optimized.json) を保存した。入力 hash・件数・checksum は全24 Reader/投影の組み合わせで新旧一致する。密な10万セルの4ケースでは所要時間が短縮し、割り当ても減少した。このホストの改善後測定では全6ケースで ClosedXML より短い中央値となったが、別入力・実行環境への保証ではない。少数セルでは初回コストと測定変動の影響が大きく、疎/選択ケースの差を大幅な追加高速化と扱わない。ClosedXML の再測定値にも変動があるため、対照値も表に併記した。

## 残るコストと範囲

メタデータと本文の2回走査、SDK による leaf text の生成、ExcelNumberFormat への object 入力、セルスナップショットは残る。初回 JIT を含む少数セルの読取では固定コストが支配的になる。SST 全体・書式・行列メタデータ・結合・数式グループは引き続き保持するため、定メモリやプロセス RSS 上限は保証しない。速度比は CI の固定合否にしない。

今回も描画変換には接続していないため、Core/Main 経由の新 Reader の PDF/PNG/SVG/Markdown 出力や Excel 実機の見た目一致を受入証拠にしていない。数式再計算・共有式の相対参照展開・高度な spill・画像等の移管・未検証の地域依存書式は引き続き対象外。既知の elapsed time 表示差分も依存ライブラリの出力を維持する。
