単色データバーと基本3アイコンセットを、Readerから全視覚出力・Mappingまで実装します。

- 通常／GUID連携／独立x14データバー、正負の色・軸・方向・枠線・showValueを対応。既存の母集団と閾値キャッシュを共有します。
- 3Arrows、3ArrowsGray、3TrafficLights1、3Symbols、3Symbols2を固定ベクターで描画。PDF／PNG／SVG／PostScript、公開レイアウト、連続PNG／SVGを対応します。
- rich textと装飾の不変payloadを保持し、文字計測・ContentBoundsの両方でアイコン余白を確保します。値・数式・Markdownは保持します。
- Mappingでx14のxm:sqrefとcfvo式、GUIDペア、優先度、集計unionを保持し、安全に移動できない構造変更は保存前に拒否します。
- Coreは条件付き書式を評価せず、通常は恒等／no-opの汎用フックのみ追加。新規プロジェクト・依存パッケージ・CLIオプション・Excel COMの実行時依存はありません。

グラデーションは警告付きで単色へ近似します。負数補間・同値・軸・色・形状は計画の固定Phase3プロファイルです。4／5アイコン、3TrafficLights2、Flags、custom/cfIconは未対応です。Excel実機比較は未実施です。

検証結果と再生成可能fixture、性能の生データ・手順は `docs/validation/conditional-formatting-phase3/README.md` を参照してください。

検証: Release再ビルドは警告・エラー0、全2,817件成功、Coreカバレッジ行82.78%／分岐71.27%、Reader/Core単独依存検証・API互換性・PDF/PS計18ファイルの外部解析が成功しました。性能の割当量と時間は別判定とし、5%の時間目標を一貫確認できないケースは未確認として、生データとともに記録しています。
