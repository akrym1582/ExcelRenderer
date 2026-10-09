# Mapping の大量データ性能確認

確認日: 2026-10-09。対象コミット: `22a101fd39fa5eb515ff9b1b8ed3d967c5af74e6`。
ライブラリの実装は変更せず、計測用スクリプトと結果を追加した。

## 判断

単純な `**items[*].property` の1行配列展開は、10列・約10万件まで
XLSX の生成と内容検証に成功した。一方、`@start-array` / `@end-array`
で囲む明示的ブロックは、同じ出力行数でも処理時間が大幅に増える。
大量配列に対して、すべてのテンプレート形式で問題ないとは判断できない。

大きな明細一覧は、1行に `**items[*].property` を並べる形式を優先する。
明示的ブロックやネストを必要とする帳票は、想定する親件数・子件数の両方で
計測する。行数だけでなく列数とテンプレート構造が処理負荷を左右する。
約10万件の生成は長時間の同期処理になるため、リクエスト内で実行する場合は
許容時間と並列実行数を実測に合わせる必要がある。

## 結果

基本ケースは各1回の実測値。CLR の99,998件は追加で2回測定した。
成功と記載したケースは、出力の再読込検証も通っている。

| 配列件数 | CLR 時間 | CLR ピーク | JSON 時間 | JSON ピーク |
| ---: | ---: | ---: | ---: | ---: |
| 1,000 | 0.58秒 | 94 MiB | 0.57秒 | 95 MiB |
| 10,000 | 2.36秒 | 131 MiB | 1.94秒 | 149 MiB |
| 50,000 | 5.76秒 | 302 MiB | 6.04秒 | 403 MiB |
| 99,998 | 10.66秒 | 496 MiB | 11.27秒 | 704 MiB |

CLR の99,998件は3回とも成功。時間は 10.56〜10.89秒、
中央値 10.66秒、ピークは 488〜496 MiB。
出力サイズは約4.05 MiBだが、処理中の累積マネージド割当は約13.4 GiBだった。
GC が回収するため、13.4 GiBが同時に必要になるという意味ではない。
JSON のピークには JSON 生成・入力準備の影響もあるため、この差を mapping の
内部実装だけのメモリ差とは解釈できない。

| テンプレート形式 | 件数 | 時間 | ピーク | 判定 |
| --- | ---: | ---: | ---: | --- |
| Dictionary・50列 | 10,000 | 3.76秒 | 240 MiB | 成功 |
| 明示的ブロック | 100 | 0.67秒 | 90 MiB | 成功 |
| 明示的ブロック | 300 | 3.63秒 | 94 MiB | 成功 |
| 明示的ブロック | 1,000 | 64.55秒 | 108 MiB | 成功 |
| ネスト（親100×子10） | 1,000 | 8.69秒 | 97 MiB | 成功 |
| 数式追加 | 1,000 | 0.76秒 | 103 MiB | 成功 |
| 数式追加 | 10,000 | 2.94秒 | 167 MiB | 成功 |
| ネスト（親1,000×子10） | 10,000 | 90秒で打切り | 141 MiB（採取RSS） | 未完了・内容未検証 |
| 結合追加・右側に通常セルあり | 10,000 | 12.14秒 | 153 MiB | 成功 |

ブロックは100→300→1,000件で0.67→3.63→64.55秒と、件数に比例しない増加がある。
1,000件の累積割当は約200.7 GiBで、同じ件数の単純 CLR 配列の約148 MiBより
大きい。短期メモリのピークだけを見ると、GC の負担と長い処理時間を見落とす。

100,000件の CLR 配列は既定の行数上限で約0.65秒で拒否され、出力は0バイトのまま。
99,998件ではヘッダー・フッター込み100,000行になり成功した。
100万行、複数シートの同時大量展開、並列実行、画像入り帳票は未計測。

## 結合セルで見つかった不具合

右端に `K2:L2` の結合セルを置き、`K2` にだけ値を入れるケースでは、
1,000件・10,000件とも生成後の結合数検証に失敗した。
2件の最小再現でも結合が2つ必要なところ1つしか残らなかった。
結合の右側 `M2` に通常セルを追加すると、2件・10,000件とも結合数検証が成功した。
したがって、生成時間が短くても、この右端結合ケースは正常処理と評価しない。
ライブラリ側の修正は今回行っていない。

小さなテンプレートで直接確認すると、`LastColumnUsed(XLCellsUsedOptions.All)` は
11（K列）、結合範囲の右端は12（L列）だった。`TemplateSheet.LastColumn` は
前者だけで幅を決め、`WorksheetExpansion.DuplicateRows` はその幅で `CopyTo` する。
このためコピー対象が結合の右端まで届かない。結合範囲の右端もテンプレート幅へ
含める処理と、右端結合の回帰テストが修正候補になる。

```bash
# 結合数不一致を検出するため終了コード1になる
python tools/ExcelRenderer.Performance/mapping-benchmark.py --output /tmp/merge-edge.jsonl --mode merged --rows 2 --runs 1
# 右側に通常セルがある比較ケース
python tools/ExcelRenderer.Performance/mapping-benchmark.py --output /tmp/merge-contained.jsonl --mode merged-contained --rows 2 --runs 1
```

主計測のスクリプト終了コード1は、ネスト10,000件のタイムアウトと
右端結合の検証失敗を反映する。基本の CLR / JSON ケースはすべて成功した。

生データ:
[主計測](../tools/ExcelRenderer.Performance/results/mapping-20261009.jsonl)、
[環境・条件](../tools/ExcelRenderer.Performance/results/mapping-20261009.metadata.json)、
[CLR 再測定](../tools/ExcelRenderer.Performance/results/mapping-repeat-20261009.jsonl)、
[右端結合の最小再現](../tools/ExcelRenderer.Performance/results/mapping-edge-20261009.jsonl)、
[通常セル追加・2件](../tools/ExcelRenderer.Performance/results/mapping-contained-small-20261009.jsonl)、
[通常セル追加・10,000件](../tools/ExcelRenderer.Performance/results/mapping-contained-20261009.jsonl)。

## 条件と計測方法

- Debian 13 / Linux x64、.NET SDK 10.0.401、ランタイム 10.0.12、Release。
- コンテナ上限: CPU 2コア相当、メモリ8 GiB。
- 各ケースは新しいプロセス。JIT を含む初回呼び出しで、事前の mapping ウォームアップは行わない。
- テンプレートはヘッダー、配列の本文、フッター。本文には共通の背景色と高さ22の行設定。
- 10列の本文は識別文字列1列と数値9列。幅広ケースは50列。
- `clr`: 公開プロパティを持つ C# オブジェクト配列。`json`: 同内容の JSON 文字列。
- `dict`: 文字列キーの Dictionary 配列。`block`: 1行の本文を明示的配列ブロックで囲む。
- `nested`: 親1件に子10件の二重ブロック。件数は子の総数。
- `merged`: 本文の右端に横2列の結合を追加。`merged-contained`: その右側に通常セルを追加。`formula`: 本文に `B行*2` を追加。

時間は `Map` / `MapJson` の呼び出し全体。テンプレート読込、全行の評価、
行展開、数式評価、XLSX 保存、出力ストリームへのコピーを含む。
入力配列・JSON・テンプレートの生成、および出力の再読込検証は時間に含めない。
PDF / PNG への変換は測定していない。

メモリは呼び出しが戻った時点でのプロセスの lifetime peak working set と、
Python 側で10 msごとに採取した RSS を記録する。どちらも起動・入力準備を含み、
mapping だけの追加メモリ量ではない。RSS の採取は `mapped` の出力を検知するまで。
短時間のピークや、検知までのごく短い検証開始部分にはサンプリングの誤差がある。
`allocatedBytes` は呼び出し中のマネージド累積割当量で、ピークメモリとは異なる。

出力は ClosedXML で再読込し、最終行数、先頭・末尾の文字列、末尾数値、
フッター位置、背景色、行高を検証する。結合ケースは結合数、数式ケースは
末尾の数式の相対参照と計算結果も確認する。全セルの網羅比較ではない。

## 実装から分かる制約と改善候補

`WorksheetExpansion.Plan` は全出力行の `XLCellValue` を保持し、全シートの
計画が完了した後に `Apply` する。`DataPath.Array` も IEnumerable / JSON 配列を
リストまたは配列に実体化する。ストリーミングで一定メモリに収める方式ではない。

単純配列は `DuplicateRows` で追加行をまとめて挿入した後、本文を各行へコピーする。
明示的ブロックでは開始・終了マーカーを含めてコピーし、各コピーについて
`ApplyNodes` が開始行と終了行を `DeleteRows` で削除する。ネストではさらに
子ブロックの挿入・削除が加わる。大きなシートに対する反復的な構造変更と
参照更新が、ブロック形式の優先的な改善対象になる。各 ClosedXML 内部処理の
CPU プロファイルまでは取得していないため、内部関数別の寄与率は未確定。

保存は常に `EvaluateFormulasBeforeSaving = true`。計画、展開済み workbook、
保存用の MemoryStream、呼び出し元の出力ストリームが重なる。
ファイル API も内部で MemoryStream を使うので、ファイルへ出すだけでは
ストリーミングにはならない。

Dictionary のメンバー取得は、大文字・小文字を厳密に扱うためキーを列挙してから
値を引く。CLR の取得はセル評価ごとに型・インターフェース・プロパティを調べる。
列数の多い Dictionary や反復する CLR パスには改善余地があるが、変更する場合は
既存の名前の大小文字と型保持の仕様を維持する必要がある。

`MaxOutputRows` の既定値100,000は、配列件数ではなくシート全体の出力行数。
このテンプレートではヘッダーとフッターが2行なので、99,998件が上限になる。
列数、シート総数、数式・結合数、バイト数の上限ではない。
Excel の最大行数1,048,576を上限に設定できるが、明示的ブロックでは
マーカーを含む中間展開が先に Excel の行数制限へ達する可能性がある。

キャンセルは行の計画やコピーの反復などで確認されるが、配列の列挙中や
ClosedXML の単一の挿入・削除・保存処理中には確認されない。
CancellationToken は厳密な処理時間の上限にはならない。

## 再現

```bash
python tools/ExcelRenderer.Performance/mapping-benchmark.py --output /tmp/mapping.jsonl --runs 1
python tools/ExcelRenderer.Performance/mapping-benchmark.py --output /tmp/mapping-clr.jsonl --mode clr --rows 99998 --runs 3
python tools/ExcelRenderer.Performance/mapping-benchmark.py --output /tmp/mapping-block.jsonl --mode block --rows 1000 --runs 1 --timeout 90
```

Python 3、.NET 10、Linux の `/proc` が必要。スクリプトは一時ディレクトリで
実行ファイルをビルドし、終了時に削除する。各プロセスの標準出力・エラー、
終了コード、タイムアウトも JSONL に記録する。既存の結果ファイルには追記する。
タイムアウトと検証失敗はスクリプト全体を終了コード1にする。
同時実行で CPU や GC の競合が起きないよう、ケースは逐次実行する。

## 回帰確認

```bash
dotnet build ExcelRenderer.slnx -t:Rebuild --no-restore --verbosity:minimal -m:1
dotnet test ExcelRenderer.slnx --no-restore --verbosity:minimal -m:1
```

再ビルドは警告0・エラー0。Core 76件、Mapping 32件、本体410件、Tool 44件の
合計562件が成功した。テスト実行を終えてから本計測を開始し、CPU 競合を避けた。
