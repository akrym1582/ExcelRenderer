# ExcelRenderer 数式機能拡張・疎範囲評価・正解比較・フォルダ整理 一括実装計画

作成日: 2026-10-17
対象: https://github.com/akrym1582/ExcelRenderer
調査基準main: b2d42c9c17293085e29f2f6fde50f9bcea9fa7dc

## 1. この指示で完成させるもの

最新mainから作業ブランチを作成し、以下を一つの変更セットとして最後まで実装する。

1. 空セルを一件ずつ読まない疎な範囲評価。全列参照を実用化する。
2. SUMIF、SUMIFS、COUNTIF、COUNTIFS。
3. 完全一致のVLOOKUP、MATCHと単一セルを返すINDEX。
4. 日付6関数と文字列8関数（下記で範囲を固定）。
5. Excelが再計算した値・型・エラーとの比較テスト基盤、正解取得ツール、差分報告。
6. FormulasとOpenXmlのソースを責務ごとのフォルダへ整理。

既存19関数＋新規21関数＝合計40関数を目標とする。「日付・文字列を全部」など無制限な解釈はしない。動的配列・近似検索・外部ブック・VBAは対象外。
製品の既存統合、公開API、NuGet依存方向、CachedValue既定値を維持する。独立ライブラリ時代の「他csprojから参照しない」制約は現在には適用しない。

この計画はコード／一次資料の調査による。計画作成時にC#実行やExcel実機による正解取得はしていない。実装時の実施記録と区別する。

## 2. 作業開始と変更管理

```bash
git status --short
git fetch origin
git switch -c feature/formulas-report-functions-and-sparse-ranges origin/main
git rev-parse HEAD
```

- Codex Cloudなど別環境でも最新取得後に開始。既存変更を破棄しない。同名ブランチを強制上書きしない。
- AGENTS.md、下位指示、docs/formulas*.md、既存テスト、依存チェックを読む。
- 基準SHAは調査用。古いSHAへリセットしない。既に実装済みの項目は重複実装せず検証で確認する。
- フォルダ移動だけのコミットを最初に分け、その後機能を段階的に実装する。ユーザーへの確認待ちで各段階を止めない。
- mainへ直接書き込まない。公開、リリース、マージは対象外。
- 正解実測が環境上できない場合も、それ以外の全実装・検証を進め、実測の未完了を明示する。架空のExcel正解で完了扱いにしない。

## 3. 確認済みの現状

- Formulasはnetstandard2.1、BCLのみの計算ライブラリ。テスト実行は.NET 10。
- FormulaParserはTokenKindを導入済み。文字列・演算子混同の修正を維持。
- DecimalRoundingは最短ラウンドトリップ十進表現で丸める。既存の限定互換プロファイルを勝手に変更しない。
- FormulaSessionは範囲内の全座標をReadし、空値もvalues辞書に保持する。既定MaxCellVisitsは1000000。
- IFormulaDataSourceはTryResolveSheet/GetCell/TryResolveNameの3メソッド。既存外部実装に新しい必須メソッドを追加しない。
- WorkbookCalculationSessionはOpenXmlのスナップショットを計算器へ適合する。WorkbookInfo.Date1904は既に存在する。
- 文字列一値上限32767 UTF-16単位、キャッシュText合計16777216の保護を導入済み。
- OpenXmlは既にExcelNumberFormatに依存するが、FormulasからOpenXmlやExcelNumberFormatを参照させない。
- 既存の検証／性能記録は過去SHAの証拠として保存し、今回の結果で上書きしない。

## 4. フォルダ整理

### 方針

ファイルを移動しても既存の公開namespace・型名・assembly・署名を変えない。Formulasの既存型はExcelRenderer.Formulas、OpenXmlの既存型は元のnamespaceを維持する。
フォルダとnamespaceの一致だけを目的に利用側のusingを大量変更しない。既存partialクラス群は同じ責務のフォルダにまとめる。
既存Input/とProperties/は維持。csproj直下には原則csproj、packages.lock.json等のプロジェクト管理ファイルだけを残す。

### Formulasの配置

| フォルダ | 既存ファイル／役割 |
|---|---|
| Syntax/ | FormulaParser、FormulaNode、FormulaNodeKind、FormulaParseResult、ParsedFormula、SourceSpan |
| References/ | FormulaCellAddress、FormulaRange、FormulaRewriter |
| Values/ | FormulaValue、FormulaValueKind、FormulaCellData |
| Evaluation/ | FormulaEngine、FormulaSession、FormulaEvaluationOptions、FormulaEvaluationResult |
| Diagnostics/ | FormulaDiagnostic |
| Data/ | IFormulaDataSource、InMemoryFormulaWorkbook、新しい疎範囲契約・索引 |
| Functions/Numeric/ | DecimalRounding、数値関数 |
| Functions/Aggregation/ | 既存集計と条件付き集計 |
| Functions/Logical/ | IF/IFERROR/AND/OR/NOTなど |
| Functions/Lookup/ | VLOOKUP/MATCH/INDEX |
| Functions/DateTime/ | 日付関数・日付シリアル変換 |
| Functions/Text/ | 文字列関数・限定TEXT書式処理 |
| Criteria/ | CriteriaParser、CriteriaMatcher、WildcardMatcher |

FormulaSessionに新関数の巨大switchを追加し続けない。評価制御・循環・キャッシュ・予算はSessionに残し、関数群とCriteriaを内部ヘルパーへ抽出する。初期化に反射を使わない。
関数名、最小／最大引数数、特殊評価方式を一つの内部レジストリで管理し、静的検査と実行側の対応関数がずれないようにする。IFの遅延評価を一般的な全引数先行評価へ変更しない。

### OpenXmlの配置

| フォルダ | 既存ファイル |
|---|---|
| Model/Cells/ | CellAddress、CellRange、CellRecord、CellValue、CellValueType、FormulaInfo、RichTextRunInfo |
| Model/Workbook/ | WorkbookInfo、WorksheetInfo、SheetInfo、DefinedNameInfo、ElementInfo、RowInfo、ColumnInfo |
| Model/Styles/ | CellStyleInfo、FontInfo |
| Model/Drawing/ | ImageInfo |
| Model/Printing/ | PrintRangeInfo、PrintReferenceInfo |
| Reading/ | IWorkbookDataReader、OpenXmlReaderOptions、OpenXmlWorkbookReaderと全partial |
| Writing/ | OpenXmlWorkbookWriterと全partial、WorkbookWriteCapabilities、RowPrototypeCache |
| Writing/Plans/ | WorkbookEditPlan、SheetEditPlan、CellValueEdit、OutputRowOccurrence |
| References/ | StructuralReferencesとFormulas partial、PrintReferences |
| Calculation/ | WorkbookCalculationSession、WorkbookRecalculator、FormulaTextResolver、WorkbookFormulaInspection、FormulaCalculationMode、FormulaCalculationException |
| Formatting/ | CellDisplayResult、NumberFormats、StyleTable |
| Diagnostics/ | ReaderDiagnostic、ReaderDiagnosticCodes、OperationMetrics |
| Infrastructure/ | LeaveOpenStream、WorkbookRelationships、XmlValues |
| Input/、Properties/ | 既存配置を維持 |

最新mainで増えたファイルも同じ分類に割り当てる。判断結果をdocs/source-organization.mdに一覧化する。
スクリプトのパス、mutation対象、ドキュメントのリンク、Compile Include/Remove、リンクソース、パッケージ検査が旧位置を前提としていないかrgで確認する。
移動直後に依存検査と全体ビルドを通す。ファイル移動と同時に無関係な挙動や整形を変えない。

## 5. 疎範囲評価

### 5.1 追加契約

IFormulaDataSourceを壊さず、任意実装のISparseFormulaDataSourceを追加する。

- 指定矩形内の「保存された値／式を持つ座標」を重複なし・行優先順で逐次列挙する。
- 戻り値は座標のみを基本とする。値／式の評価は必ず既存Session.Readを通し、循環・診断・Text上限を迂回しない。
- 列挙漏れがないことを契約化する。式が空文字／0／エラーを返すセルも列挙対象。
- 書式しかないBlankは欠落セルと同じ意味なので列挙しなくてよい。計算メタデータを持つセルは省略しない。
- CancellationTokenを受け、範囲外・重複・順序違反はデータソース診断にする。
- 列挙に対応しない外部DataSourceは既存の密な走査へフォールバック。既存の資源制限も維持する。

InMemoryFormulaWorkbookとOpenXmlの計算スナップショットに疎範囲契約を実装する。
シート別のソート済み行索引＋行内列索引を一度構築する。クエリごとに全ブックDictionary.Whereを実行しない。メタデータだけの巨大UsedRangeを走査上限に使わない。

範囲の行数×列数、相対位置、空白数はlongで計算。全シート範囲はintを超える。数式セルを含む範囲から自分自身を黙って除外しない。

### 5.2 既存関数

SUM/COUNT/COUNTA/MIN/MAX/AVERAGE/AND/ORの参照引数は疎列挙へ移行する。各関数で欠落Blankを無視できることを確認して使用する。MIN/MAXの対象数0、AVERAGEの#DIV/0!、AND/ORの有効値0などを維持する。
スカラーのセル参照は従来通り。空セルをReadした場合も辞書に大量に保持しない。数式が返した数値0や空文字を欠落セルと混同しない。

MaxCellVisitsは実際に検査／評価したセル座標を数える。100万件の欠落セルを一括処理したときに100万回分を課金しない。
範囲カーソルの進行や条件照合をMaxEvaluationStepsに計上し、取消しを確認する。全列参照への対応を単なる上限引上げで済ませない。
統計として実読取数・範囲候補数・空白一括処理数・キャッシュ件数をテスト／性能計測から確認できるようにする。不要なグローバル監視APIは作らない。

### 5.3 条件範囲をずらして整合させる

異なる開始行／列の範囲を比較するときは、絶対座標ではなく各範囲の左上からの相対位置で結合する。
各criteria_rangeと必要なsum_rangeの疎座標を同じ相対indexへ変換し、ソートされたカーソルをk-way mergeする。候補の和集合に存在しない位置は、すべての関連範囲がBlankの区間として扱う。
例えばCOUNTIFSの全条件がBlankに一致する場合は、この区間長を件数へ加算する。SUMではsum_rangeだけ値がある位置も候補に含める。
全列のbool配列や全座標HashSetを作らない。候補数／条件数に応じたメモリに抑える。
最適化版と小範囲の単純な密評価版で、値・型・エラー・評価失敗を比較する。

## 6. 条件付き集計4関数

| 関数 | 引数と仕様 |
|---|---|
| COUNTIF(range, criteria) | 範囲は参照。条件に一致する位置数 |
| COUNTIFS(range1, criteria1, …) | 1～127組。同じ行数・列数の範囲。各位置で全条件一致 |
| SUMIF(range, criteria, [sum_range]) | 省略時rangeを合計。指定時はsum_range左上からrangeと同じ寸法を使う |
| SUMIFS(sum_range, range1, criteria1, …) | 1～127組。すべて同じ行数・列数 |

SUMIFはSUMIFSと違いsum_rangeの見かけの大きさが違っても左上基点で寸法を合わせる。拡張先がシート外なら#REF!。SUMIFS/COUNTIFSの寸法不一致は#VALUE!。
IF系という名前を理由に全引数の評価方法をIFと共通化しない。

### Criteria基盤

条件は数値・真偽値・Text・単一セル参照・式の単一結果を受け付ける。複数セル条件配列は未対応診断。
Text条件の先頭から最長一致で <= >= <> = < > を解析し、残りを数値／文字列条件へ解釈する。演算子なしは等価条件。
ワイルドカードは*、?、エスケープ~。比較は既存プロファイルのOrdinalIgnoreCase。正規表現への単純置換は避け、バックトラッキング爆発を防ぐアルゴリズムにする。
日付条件は数値シリアルまたは `">="&DATE(...)` に対応。ロケール依存の日付文字列の自動解釈は対象外。
数値文字列と数値の条件照合規則は通常の二項比較と別にする。Criteriaを事前コンパイルしてセルごとに再解析しない。

### 固定すべき互換性マトリクス

実装前にfixturesで以下を表にする。Excel実測が得られればそれを基準にし、資料からしか確定できない項目は出典を明示する。

- 値: 未設定Blank／明示Blank／式の空文字／0／数値／数値Text／通常Text／Boolean／7種類のエラー。
- 条件: 0、"0"、""、"="、"<>"、">0"、"<>0"、TRUE、"TRUE"、"*"、"?"、"~*"、"~?"、"~~"、"#N/A"、空セル参照。
- COUNTIFSの条件が空セル参照の場合は0として扱う（Microsoftの明記あり）。空文字条件と混同しない。
- 条件セルのエラー、条件範囲のエラー、選択されたsumセルのエラー、非選択sumセルのエラーを別ケースにする。
- 条件が複数ある場合の、後続条件のエラー／未対応式の扱いを密評価と疎評価で一致させる。
- 数式エラー値は通常値として関数固有規則に従い、エンジンの循環・未対応・上限は0や不一致に変換しない。

細かな型／エラー互換性は資料だけで断定できない。未確認の規則を「Excel互換」と宣言しない。実測なしで暫定仕様を置く場合はprofileケースとして明記し、oracle未完了項目に残す。

## 7. 完全一致検索3関数

| 関数 | 今回の対応 |
|---|---|
| VLOOKUP(value, table, column, FALSE) | 第4引数false／0の完全一致。先頭列を上から検索 |
| MATCH(value, vector, 0) | 1行または1列の参照、最初の一致位置を1始まりで返す |
| INDEX(reference, row, [column]) | 単一領域から一つのセル参照を返す。1次元では適切な添字省略を許可 |

- VLOOKUPの第4引数省略／TRUE、MATCHの第3引数省略／±1は近似検索である。勝手に完全一致へ変更せずUnsupportedFeature等の既存方針に沿ったエンジン診断を返す。
- 文字列は大文字小文字を無視。数値と数値Textは同一にしない。重複は最初の一致。
- VLOOKUP/MATCHのText完全一致では* ? ~をCriteriaと共有するワイルドカード処理へ接続する。ただし型照合ルールまで条件付き集計と共用しない。
- 見つからなければ#N/A。VLOOKUP列番号が表幅より大きい場合#REF!、1未満#VALUE!。添字の小数の扱いをMicrosoft資料／oracleで固定する。
- INDEXの0添字による行／列全体返却、複数領域、配列入力は今回は未対応診断。単一セルのINDEXは内部Referenceを保持し、SUM(INDEX(...))などで参照経由の型規則を維持する。
- 検索キーBlank／0／空文字、検索範囲エラー、未一致位置のエラー、返却セルがBlankの場合をoracle対象にする。
- 疎検索で空白に一致するキーを扱うとき、欠落区間の最初の位置も候補にする。存在セルだけ検索して空白一致を失わない。
- MATCHは横方向も対応。INDEXの2次元・1次元、範囲起点がA1でないケースを検証する。
- 非一致行の返却列は不要に計算しない。検索列にある数式は必要な検索順で評価する。
- O(検索行数)の逐次処理から開始し、同じ検索表への反復が多いベンチマークを追加する。無制限の検索索引キャッシュは作らない。

## 8. 日付6関数

対象はDATE、YEAR、MONTH、DAY、EDATE、EOMONTH。TODAY/NOWは基準時計とタイムゾーン設計が別途必要なため今回は追加しない。DATEVALUE等のロケール日付解析も対象外。

### 日付コンテキスト

FormulaEvaluationOptionsにDateSystem（Excel1900/Excel1904、単独既定Excel1900）を追加してセッションで固定する。
OpenXml統合ではWorkbookInfo.Date1904から設定し、外部Optionsによる矛盾を避けるためブック値を優先する。呼出し元Optionsインスタンス自体は変更しない。
シリアル値はNumberのまま扱い、シート書式で日付へ勝手に型変換しない。
ISO日付セルは明示的なISO形式を読み、日付系に応じたシリアルへ変換する。時刻小数は保持。タイムゾーン付きISOをローカル時刻へ暗黙変換せず、今回未対応診断にする。

### シリアル変換

Excel1900の架空の1900-02-29（serial 60）を専用の年月日表現で保持する。DateTime.FromOADateだけで実装しない。
Excel1904のserial 0=1904-01-01。1900系のserial 0のYEAR/MONTH/DAYとDATE生成の扱いは専用fixtureで固定する。
1900系の59/60/61、1904系の0、同じ日付の1462差、9999年末、負数・範囲外・時刻付き値を比較する。
DATEの年0～1899の補正、月／日のはみ出しを正規化。月／日の入力が極端な場合はループで回さず境界検査と算術で処理する。
EDATEは月移動先に同じ日がなければ月末へ、EOMONTHは移動先の月末へ。monthsの小数は0方向に整数化。日付外は#NUM!、変換不能Textは#VALUE!。
既存の数値Text変換は使えるが、"2026/10/17"を日付として勝手に読むことはしない。
架空日付に対する各関数の結果はExcel実測により固定する。未確認のまま.NET暦の結果で置き換えない。

## 9. 文字列8関数

LEFT、RIGHT、MID、LEN、TRIM、SUBSTITUTE、CONCATENATE、TEXTを追加する。CONCAT/TEXTJOINと範囲展開は今回対象外。

| 関数 | 仕様 |
|---|---|
| LEFT/RIGHT | 第2引数省略は1、0は空文字、負数#VALUE!、長さを超えれば全文 |
| MID | 開始位置1始まり、開始<1または長さ<0は#VALUE!、末尾より後は空文字 |
| LEN | 文字列長。数字等のText化は既存の連結プロファイルを利用 |
| TRIM | ASCII U+0020の前後除去と連続スペース圧縮。タブ・全角空白・NBSPを勝手に消さない |
| SUBSTITUTE | 大文字小文字を区別。第4引数省略は全置換、指定は正の出現番号。0以下#VALUE!。old_text空は元の文字列 |
| CONCATENATE | 1～255個の単一値。参照範囲の自動展開はしない |
| TEXT | 数値＋下記限定書式。結果はText |

LEN/MID等にはExcelの互換性バージョンによるsurrogate pairの差がある。今回はUTF-16コード単位を数える互換性v1プロファイルとして固定し、正解ブックもv1に揃える。絵文字を含むv2観測は別プロファイルとして記録し、差を隠さない。BMP日本語は通常どおり。文字列資源制限は既存どおりUTF-16で統一する。
文字列関数の整数引数は0方向に整数化し、int変換前に長さと範囲を検査する。

### TEXTの限定書式

FormulasをBCLのみで維持するため、独自の小さいformatterを実装する。OpenXmlを逆参照したり、数式TEXTにだけ環境の現在カルチャーを使ったりしない。
今回必須の形式は以下に固定する。

- 数値: 0、0.00（0の個数で小数桁）、00000（ゼロ埋め）、#,##0、#,##0.00、0%、0.00%。
- 日付: yyyy-mm-dd、yyyy/mm/dd、yyyymmdd、yyyy年m月d日、yyyy年mm月。
- 半角英字の大小を正規化し、引用リテラルを扱う。上記の構文要素から成る形式に限定する。
- 数値の小数点は.、桁区切りは,に固定。月名・曜日名・和暦・言語コード・色・条件付きセクション・会計・分数・科学表記・時刻書式は未対応診断。
- 数値の表示丸めは既存DecimalRoundingと整合させる。負数の符号、ゼロ埋め、%の倍率、日付系とserial60をテストする。
- 書式を.NET書式へ無検証に転送しない。対応外の書式を近似して成功扱いしない。

文字列生成はすべて既存のMaxStringLengthとMaxCachedStringCharactersを通す。SUBSTITUTE/CONCATENATE/TEXTでは出力長をlongで見積もり、巨大な割当て前に止める。StringBuilderを最後だけ検査する実装は不可。

## 10. Excel再計算結果との比較テスト

### 10.1 成果物

- tests/ExcelRenderer.Formulas.Tests/Fixtures/Oracle/ のケース定義と正解JSON。
- tools/ExcelRenderer.Formulas.Oracle/ のケースブック生成、正解抽出、比較CLI（製品とは別の開発ツール）。
- scripts/capture-formula-oracle.ps1 のWindows Excelデスクトップ用正解生成スクリプト。
- 純粋エンジン比較、XLSX読取→再計算→保存→再読取比較、Mapping行展開後の比較。
- docs/formulas-oracle.md、ケースカバレッジ表、差分レポート。

ツールには既存OpenXml等を使用してよい。製品FormulasのBCL境界を壊さない。

### 10.2 本物の正解の作り方

ケース定義から自作の小さなxlsxを生成する。Excelへの入力は定数の型を保持し、Text("001")やText("2")を数値へ自動変換させない。
WindowsのExcel COMを専用プロセスとして起動し、外部リンク更新を止め、マクロを無効化し、自作のマクロなしブックだけを開く。既存ユーザーのExcelインスタンスを閉じない。
CalculateFullRebuildを実行し、計算完了を待って保存、閉じる。タイムアウト／失敗を検出し、finallyで自分が作ったCOM参照を解放する。全EXCELプロセスをkillしない。
再計算済みxlsxのf/v/tを読んで、結果のNumber/Text/Boolean/Errorを抽出する。セルの見かけの表示文字列だけを正解にしない。
ExcelのVersion/Build、OS、取得日時、カルチャー、Date1904、互換性バージョン、入力ケースSHA256、保存後xlsxのSHA256をmanifestに記録する。

自作エンジンで出した値、手計算値、元xlsxの未検証キャッシュ、LibreOffice結果をexcel-observedと記載しない。LibreOfficeは任意の二次比較用で、Excel正解の代替にはしない。

### 10.3 Excelを使えないCodex環境

最初に利用可能性を判定し、Excelがなければ自動インストールや資格情報要求をしない。コード・ケース生成・抽出・比較・通常テスト・統合・性能検証をすべて完成させる。
既存の真正なExcel正解fixtureがあればそれを利用する。なければsource=specification/hand-calculated/profileで通常テストを作り、Excel実測manifestを偽造しない。
一回のWindows操作で正解取得できる手順を用意し、strict比較モードはExcel正解が0件／必須カテゴリ欠落なら失敗させる。通常CIでExcel未導入が「正解比較成功」に化けないよう、実測件数0と未検証を明示する。
Excel実機がなく正解が取得できなかった場合、実装完了と実測検証未完了を分けて報告する。この制約はコードを途中で放棄する理由にはしない。

### 10.4 比較ポリシー

- 型、Text、Boolean、エラーコードは完全一致。Blankと空文字、Number(0)とBoolean(false)を区別。
- 整数、日付の整数serial、件数、明示した丸め結果は原則厳密比較。
- 非整数の数値許容差はケースごとにabs/relを記録し、許容差を後から広げて失敗を消さない。
- 未対応診断は通常Excelエラーの一致として扱わない。
- 既存の最短十進丸め／Ordinal照合／UTF-16仕様など既知差分は専用のcompatibility-exceptions.jsonに個別ケースID・理由・観測値・採用仕様を記載する。広いワイルドカード除外は禁止。
- レポートはmatched/mismatch/unsupported/known-difference/not-observedを別集計する。
- 一つのケースには安定ID、入力型、シート／座標、式、対象セル、期待型と値、expectedSource、根拠と環境を保持。
- データを毎回自動再生成して期待値を書き換えない。更新は明示コマンドと差分レビューで行う。

### 10.5 最低限のケース群

既存19関数の基本・型変換・前回3不具合、新規21関数の正常／境界／エラー、Criteriaマトリクス、両日付系、文字列v1を収録する。
全体で少なくとも200の独立ケースを用意し、数だけを満たす重複を避ける。Excel未実測でも全ケースを生成可能にする。
型違い・空白・ワイルドカード・範囲寸法違い・非選択エラー・月末・うるう年・serial60・上限を必須項目としてcoverage表で確認する。
小範囲の密／疎差分テストはseed固定の生成データでも追加する。これは最適化の同等性テストでありExcel互換の証明とは呼ばない。

Mappingは「行展開のみでキャッシュ無効化したxlsx」を生成し、同じファイルをExcelと独自計算器へ渡す。これで計算値比較を行う。
別途、書換え後の参照先／範囲が期待どおりかを固定式で検査する。両方に同じ誤った書換え式を渡して一致しても、参照更新の正しさの証明にはならない。

## 11. 統合・性能・回帰

### 統合シナリオ

請求書サンプルを拡張する。別シートのマスターから名称・単価取得、明細の金額・税額丸め、区分別SUMIFSと件数、締月と支払月末、TEXTによる年月表示を組み合わせる。
0/1/3/複数ページの明細行でMapping→数式更新→計算→XLSXキャッシュ→PDF/PNG/SVG/Markdownを通す。
既存のCachedValueでは計算器の新しい処理を不要に起動しない。Recalculateだけが追加関数を使用する。
不正式・上限超過・未対応モードで出力先を変更しない既存保証を維持する。

### 決定的な性能受入

- 別シートの数式からSUM(Data!A:A)を計算。Dataには10/1000/100000個の値だけ置き、既定上限で正しく完了する。
- 空列のCOUNTIF(Data!A:A,"")=1048576。列末尾の値も含め、先頭の使用範囲で打ち切らない。
- COUNTIFSで異なる開始座標を持つ等寸法範囲、全条件Blank一致、sum_rangeのみ存在値ありを確認。
- 全列に数件しかないケースで、セル訪問数／キャッシュ件数が1048576に比例しない。
- インデックス構築は実在セル数に比例、範囲クエリはシート全辞書走査を反復しないことを計測する。
- 密DataSourceのfallbackは上限で止まり、疎対応を偽って省略しない。
- 全列の自己参照はCircularReferenceになる。

### 実測

既存性能ツールに疎範囲、条件付き集計、完全一致検索、日付・文字列、混合帳票を追加。
同じ基準SHAと最終SHA、同じ入力、同じ計測方法で比較。ウォームアップ後5回中央値、割当て、GC、実セル訪問数、結果checksumを記録する。
全列の空間サイズだけを増やすケースと、実在セル数を増やすケースを分ける。
検索表1000/10000/30000件×検索回数を測り、O(n*m)のコストを隠さない。必要ならセッション限定でサイズ上限のある索引を追加し、重複先頭・型・wildcard・エラー順序を維持する。
プロセスpeak RSSと総割当てと保持メモリを混同しない。環境依存の短時間閾値をCI必須にせず、訪問数・割当て上限の決定的テストを使う。

## 12. 作業順序とテストゲート

1. 最新取得・契約／基準SHA記録。
2. ファイル移動のみ、パス参照更新、ビルドと既存テスト。
3. Oracleケースschema・生成／取得／比較ツールと互換性表。
4. 疎DataSource契約・索引・既存集計の疎評価・密疎比較。
5. Criteria基盤と4集計関数。
6. 完全一致検索3関数。
7. 日付系コンテキスト・6日付関数・ISO日付適合。
8. 8文字列関数と限定TEXT、文字列上限の統合。
9. OpenXml／Mapping／描画の一連の統合テスト。
10. Oracle差分、性能測定、依存／パッケージ／全体CI、英日ガイドと検証記録。

各段階で必要な狭いテストを実行し、最後に全体ゲートを通す。新機能の途中でユーザーの次の指示待ちにしない。
移動後のパスに合わせて既存mutation、パッケージ消費側、visual検証を更新する。閾値緩和やテスト削除で通さない。

```bash
dotnet restore ExcelRenderer.slnx --locked-mode
python3 scripts/check-formulas-dependencies.py
python3 scripts/check-core-dependencies.py
dotnet build ExcelRenderer.slnx -t:Rebuild -c Release --no-restore --verbosity:minimal
dotnet test ExcelRenderer.slnx -c Release --no-build
```

追加開発ツールで必要なlock fileだけを生成し、その後locked restoreで検証する。Formulasの新しいランタイムNuGet依存を追加しない。
AGENTS.mdと現行CIに従いpack／独立consumer／mutation／画像比較も実行する。Windows/LinuxでFormulasの同じテストを通す。Excelがないhosted runnerでCOMを実行しようとしない。

## 13. 完了条件

### 実装完了

- [ ] 4カテゴリの改善と21関数が実装され、40関数の対応表が更新された。
- [ ] Formulas/OpenXmlが責務別フォルダとなり、既存namespace・公開API互換性が維持された。
- [ ] 全列集計が疎データで既定上限内に完了し、Blank一致・密疎同等性も検証された。
- [ ] 近似検索や未対応TEXT形式を黙って別動作にしない。
- [ ] Date1904と架空日付を扱い、文字列／評価量の保護が全新関数に適用された。
- [ ] OpenXml／Mapping／描画／パッケージ消費側への統合に回帰なし。
- [ ] Oracle生成・取得・比較が再実行可能。正解の出典と既知差分を識別できる。
- [ ] 全体Rebuild警告0・エラー0、テスト成功、必須CIゲート維持、性能記録あり。

### Excel実測比較完了（別に状態を明示）

- [ ] 必須カテゴリの実際のExcel再計算結果を取得し、manifestと結果を保存した。
- [ ] strictモードで正解0件や必須カテゴリ欠落を見逃さない。
- [ ] 未説明の差分0。限定互換差はケース単位で明示、実測未実施は成功に数えない。

最終報告: 開始／検証SHA、実装内容、フォルダ移動表、テスト数、Oracle実測数・未実測数・差分、性能、未対応機能、環境制約。Excelが使えない場合は「実測比較だけ未完了」と具体的な一回実行手順を示す。
実測未完了を隠して「Excel互換検証済み」と報告しない。

## 14. 一次資料と判断根拠

- 現在コード: https://github.com/akrym1582/ExcelRenderer/tree/b2d42c9c17293085e29f2f6fde50f9bcea9fa7dc
- SUMIF（異寸法sum_rangeの左上基点）: https://support.microsoft.com/en-us/excel/functions/sumif-function
- SUMIFS（同寸法制約）: https://support.microsoft.com/ja-jp/excel/functions/sumifs-function
- COUNTIFS（空セル参照条件=0）: https://support.microsoft.com/en-us/excel/functions/countifs-function
- VLOOKUP: https://support.microsoft.com/en-us/excel/functions/vlookup-function
- MATCH: https://support.microsoft.com/en-us/excel/functions/match-function
- INDEX: https://support.microsoft.com/en-us/excel/functions/index-function
- DATE: https://support.microsoft.com/ja-jp/excel/functions/date-function
- EDATE: https://support.microsoft.com/ja-jp/excel/functions/edate-function
- EOMONTH: https://support.microsoft.com/en-gb/excel/functions/eomonth-function
- TRIM（ASCII空白）: https://support.microsoft.com/en-us/excel/functions/trim-function
- TEXT: https://support.microsoft.com/en-us/excel/functions/text-function
- Unicode互換性バージョン: https://support.microsoft.com/en-gb/excel/compatibility-versions
- Excel全再計算: https://learn.microsoft.com/en-us/office/vba/api/excel.application.calculatefullrebuild

上記資料は全境界値の正解を網羅しない。実装の期待値は仕様根拠／Excel実測／明示プロファイルに分ける。ネットワークがない環境でも本計画の仕様から実装を進め、未実測を追跡する。

