# 本体＋Coreの2エンジン構成

旧Slimを`ExcelRenderer.Core`へ改名し、共通の基盤と単一通常フォントのPDF製品を
同じプロジェクトに置きます。本体`ExcelRenderer`だけがCoreを参照します。Coreは
`netstandard2.1`の非packableプロジェクトで、本体・Fonts・Toolへの参照を持ちません。
本体とToolの既存パッケージには、同じbuildのCore DLLを同梱します。

共通input/spool、基本reader、style intern、列幅・行列座標、geometry、ページ計画、
candidate抽出、セル配置、文字placement、背景・罫線・文字コマンド、画像decode、
基本PDF描画・Save、診断集約、PDFsharp gateはCoreが所有します。

本体は`FullExcelReader`のhookで元style・VS・図形・リンク・元sheet indexを保持し、
`FullLayoutPolicy`で使用範囲を決める前に物体の幾何情報を渡します。画像はCoreが
開始ページだけ、本体がvisual boundsの交差ページへ採用します。`FullDrawingExtension`
は画像と図形を同じZ順の工程へ供給します。`FullPdfRenderer`は変形画像・図形・viewport・
完成済みrunのpainterを担当し、リンクは全宛先ページの完成後に注釈化します。

本体の公開型・interface・recordは元のassemblyに残します。拡張契約はinternalで、
friendは本体と既存テストだけです。第三の共通エンジン、公開拡張API、record継承、
型転送、DI container、plugin探索は追加しません。typed payloadはCoreから本体を
解釈する仕組みではなく、本体だけが借用した元情報を読むためのものです。

通常変換はCoreのセルをそのままlayoutとcommand生成へ渡します。選択・診断・Markdownは
順序を維持したviewを読み、全ブックを二重保持しません。範囲指定も値をコピーしない
filter viewです。利用者が作った公開sheetはstyle cache付きprojectionで受けます。
公開`ExcelReader.Read`の互換戻り値だけは公開辞書としてmaterializeします。公開passの
単独実行では、そのpassが所有するcontext情報だけを同期します。

streamingでは1ページ内だけlayout/commandsを保持し、画像・font bytesはコピーしません。
font runは再探索・再計測せず完成済みのmetricsとともに受け渡します。連続出力では
geometryを再利用してCoreのcell layerを再走査します。公開Layout APIが全ページを
返す既存契約には、active page最大1の条件を適用しません。

共有gateの区間はfont設定→計測/描画→Save→native資源解放→resolver cleanup→解放です。
converter内はsessionを借用し、独立した公開measurer/rendererは自分で取得します。
同じCore DLLを同じload contextで利用する本体/Coreが混在対象です。外部PDFsharp利用者や
別AssemblyLoadContextは同期対象外です。公開runが所有するfont snapshotはreset後も保持し、
所有者のなくなったsnapshotはweak登録により回収できます。

[最終所有者表](core-source-map.md)、[利用側の移行](core.ja.md)、[実行済み検証](core-refactor-validation.md)
を参照してください。Ubuntu/WindowsのCIを維持しますが、Linuxでのローカル結果を
Windowsでも成功した結果として扱いません。

## Reader の局所 cache と部分書式の境界

`ReaderSession` は Core の読み込み 1 回につき最大 1 個の表示用 workbook と、
上限 8,192 件の書式変換 cache を所有します。Core／本体の変換方針は別 session であり、
元の ClosedXML セル書式を変更しません。

セルの部分書式は本体の `FullCellTextData` として Core の typed payload に保持します。
Core は run のフォントを解釈せず、`IExtendedTextLayoutService` 経由で本体へ計測を委譲します。
折り返し結果の配置・縮小・印刷倍率の共通処理は引き続き Core にあります。
本体 adapter が payload の倍率を run のサイズ・座標へ一度だけ適用し、PDF／Skia は
確定済み run を再解決・再折り返しせず描画します。Core 単独の通常フォント一つ・
IVS 基底文字化・PDF の製品範囲は変更しません。

追加の文字切れ判定は Core の `CellTextDiagnostics`、診断方針・選択ページへの絞り込みは
本体 preflight が担当します。実装状況と数式表示モードの未解決事項は
[検証報告](reader-richtext-validation.md) を参照してください。

### 数式キャッシュと読取診断

Core の FormulaCacheReader が元 XML の数式結果の有無・型・生値をストリーミングで疎に保持し、
CoreExcelReader が表示ポリシーと診断候補を管理します。scratch 書式適用は ReaderSession に共通化し、
本体の公開 ExcelReadOptions は独立した Core 内部設定へ写像します。可視範囲による診断の確定は
選択機能を所有する本体 preflight が担当します。チャートシートはメタデータのみを保持し、
診断のために空白ページを追加しません。旧計測器は任意の RenderRequest.TextMeasurer で借用します。
