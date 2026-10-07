#!/usr/bin/env python3
"""Produce readable medians from the fresh-process and retention JSONL evidence."""
import argparse
from collections import defaultdict
import json
from pathlib import Path
import statistics

parser = argparse.ArgumentParser()
parser.add_argument('directory', type=Path)
args = parser.parse_args()
raw = [json.loads(line) for line in (args.directory / 'raw.jsonl').read_text().splitlines()]
groups = defaultdict(list)
for item in raw:
    groups[(item['Rows'], item['FixtureMode'], item['Format'], item['Layout'], item['Revision'])].append(item)

def median(items, key):
    return statistics.median(item.get(key, 0) for item in items)

def metric(items, key):
    return statistics.median(item.get('Metrics', {}).get(key, 0) for item in items)

print('## 新規プロセス3回の中央値\n')
print('時間は秒、メモリはMiB。文字あり=latin、罫線のみ=empty。P=ページ別、C=連続。\n')
print('| 行数 | 内容 | 形式 | 枚数 | 時間 old→new | 時間差 | peak RSS old→new | RSS差 |')
print('|---:|---|---|---:|---:|---:|---:|---:|')
for rows, mode, format_, layout in dict.fromkeys(key[:4] for key in groups):
    old = groups[(rows, mode, format_, layout, 'baseline')]
    new = groups[(rows, mode, format_, layout, 'phase3')]
    if len(old) != 3 or len(new) != 3:
        print(f'| {rows:,} | {mode} | {format_}/{layout[0]} | — | 完走不足 | — | 完走不足 | — |')
        continue
    ot, nt = median(old, 'TotalMs') / 1000, median(new, 'TotalMs') / 1000
    om, nm = median(old, 'PeakWorkingSet64') / 1048576, median(new, 'PeakWorkingSet64') / 1048576
    print(f'| {rows:,} | {mode} | {format_}/{layout[0]} | {median(new,"Pages"):.0f} | {ot:.2f}→{nt:.2f} | {(nt/ot-1)*100:+.1f}% | {om:.1f}→{nm:.1f} | {(nm/om-1)*100:+.1f}% |')

print('\n## フェーズ3の内訳（文字あり）\n')
print('preflightはbuildの1周目を含み、両列は加算しない。buildは両周の合計。private/managedは50msサンプル最大の中央値。\n')
print('| 行数 | 形式 | preflight ms | build ms | Save/encode/XML ms | peak private MiB | peak managed MiB | PNG画素 MiB | SVG buffer MiB | spill |')
print('|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|')
for key, items in groups.items():
    rows, mode, format_, layout, revision = key
    if revision != 'phase3' or mode != 'latin' or len(items) != 3:
        continue
    final = {'Pdf': 'pdfFinalSave.ms', 'Png': 'pngEncode.ms', 'Svg': 'svgNormalize.ms'}[format_]
    print(f'| {rows:,} | {format_}/{layout[0]} | {metric(items,"pagePreflight.ms"):.0f} | {metric(items,"pageBuild.ms"):.0f} | {metric(items,final):.0f} | {median(items,"PeakPrivateSampleBytes")/1048576:.1f} | {median(items,"PeakManagedSampleBytes")/1048576:.1f} | {metric(items,"pngBitmapBytes")/1048576:.2f} | {metric(items,"svgBufferMemoryPeakBytes")/1048576:.2f} | {metric(items,"svg.spillCount"):.0f} |')

supplement = args.directory / 'supplement.jsonl'
if supplement.exists():
    groups = defaultdict(list)
    retention = defaultdict(list)
    for line in supplement.read_text().splitlines():
        item = json.loads(line)
        if 'Error' in item:
            continue
        if 'PostCollectionIteration' in item:
            retention[(item['Case'], item['Revision'])].append(item)
        elif not item['Case'].startswith('retention-'):
            groups[(item['Case'], item['Revision'])].append(item)
    print('\n## 補足の中央値\n')
    print('| 条件 | 時間 old→new 秒 | peak RSS old→new MiB | decode/hit new | 読込font bytes new |')
    print('|---|---:|---:|---:|---:|')
    for case in dict.fromkeys(key[0] for key in groups):
        old, new = groups[(case, 'baseline')], groups[(case, 'phase3')]
        if len(old) != 3 or len(new) != 3:
            continue
        print(f'| {case} | {median(old,"TotalMs")/1000:.2f}→{median(new,"TotalMs")/1000:.2f} | {median(old,"PeakWorkingSet64")/1048576:.1f}→{median(new,"PeakWorkingSet64")/1048576:.1f} | {metric(new,"imageDecode"):.0f}/{metric(new,"imageCacheHit"):.0f} | {metric(new,"fontsBytesLoaded"):.0f} |')
    print('\n## 同一プロセス10回\n')
    print('100行の文字fixture。変換後のharness GC後のRSS/managed。初回から10回目の増加と、2〜10回目の範囲を分ける。\n')
    print('| 条件 | revision | RSS 初回→10回目 MiB | 2〜10回目のRSS範囲 MiB | managed 初回→10回目 MiB |')
    print('|---|---|---:|---:|---:|')
    for (case, revision), items in retention.items():
        first, last = items[0], items[-1]
        middle = [item['WorkingSet64']/1048576 for item in items[1:]]
        print(f'| {case.removeprefix("retention-")} | {revision} | {first["WorkingSet64"]/1048576:.1f}→{last["WorkingSet64"]/1048576:.1f} | {min(middle):.1f}–{max(middle):.1f} | {first["ManagedBytes"]/1048576:.1f}→{last["ManagedBytes"]/1048576:.1f} |')
