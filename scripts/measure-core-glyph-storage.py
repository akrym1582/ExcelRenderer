#!/usr/bin/env python3
"""Compare saved Core glyph-storage runtimes without overlapping build/test work."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import statistics
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--rounds', type=int, default=3)
    parser.add_argument('--controlled', action='store_true')
    args = parser.parse_args()
    work = args.directory.resolve()
    environment = os.environ.copy()
    environment.pop('CORE_PERF_DIAGNOSTICS', None)
    if args.controlled:
        environment['DOTNET_TieredCompilation'] = '0'
    prefix = 'controlled-' if args.controlled else ''
    inputs = ROOT / 'docs/validation/core-performance-optimization/inputs'
    cases = ['text1', 'text1000', 'wrap1000', 'borders1000', 'empty', 'imagesonly', 'header']
    metadata = dict(os=platform.platform(), cpu=Path('/proc/cpuinfo').read_text().split('\n\n')[0],
                    sdk=subprocess.check_output(['dotnet', '--info'], text=True),
                    inputs={name: sha(inputs / (name + '.xlsx')) for name in cases},
                    fontSha256=sha(ROOT / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'),
                    tieringDisabled=args.controlled,
                    runtimes={name: {p.name: sha(p) for p in (work / name).glob('*.dll')} for name in ['before-core', 'after-core', 'before-main', 'after-main']})
    (work / (prefix + 'environment.json')).write_text(json.dumps(metadata, indent=2) + '\n')
    records = []
    raw = work / (prefix + 'samples.jsonl')
    raw.write_text('')

    def record(value):
        records.append(value)
        with raw.open('a') as stream:
            stream.write(json.dumps(value) + '\n')

    for round_index in range(args.rounds):
        order = ['before', 'after'] if round_index % 2 == 0 else ['after', 'before']
        for name in ['clusters', 'compatibility', 'outline-80', 'outline-600', 'outline-100000']:
            for stage in order:
                workload, width = name.split('-') if '-' in name else (name, '80')
                command = ['dotnet', str(work / (stage + '-main') / 'ExcelRenderer.Performance.dll'), 'core-glyph-case', workload, '20', width]
                output = subprocess.check_output(command, cwd=ROOT, env=environment, text=True)
                samples = [json.loads(line) for line in output.splitlines()]
                record(dict(round=round_index, case='micro-' + name, stage=stage, command=command, samples=samples))
                print(round_index, name, stage, flush=True)
        for name in cases:
            for stage in order:
                target = work / (name + '-' + stage + '.pdf')
                command = ['dotnet', str(work / (stage + '-core') / 'ExcelRenderer.Core.Validation.dll'), 'render', str(inputs / (name + '.xlsx')), str(target), str(ROOT / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'), '6']
                samples = json.loads(subprocess.check_output(command, cwd=ROOT, env=environment, text=True))
                samples = [dict(milliseconds=row['Milliseconds'], allocationBytes=row['AllocatedBytes'], peakRssBytes=row['PeakRss'], gc=row['GCCollections'], cold=row['Iteration'] == 0, warmup=row['Iteration'] < 3, original=row) for row in samples]
                record(dict(round=round_index, case=name, stage=stage, command=command, samples=samples))
                print(round_index, name, stage, flush=True)
    pairs = []
    for before in (row for row in records if row['stage'] == 'before'):
        after = next(row for row in records if row['stage'] == 'after' and row['case'] == before['case'] and row['round'] == before['round'])
        for a, b in zip(before['samples'], after['samples']):
            if 'outputSha256' in a:
                assert (a['inputSha256'], a['fontSha256'], a['outputSha256']) == (b['inputSha256'], b['fontSha256'], b['outputSha256']), before['case']
        medians = {stage: {key: statistics.median(s[key] for s in row['samples'] if not s['warmup']) for key in ['milliseconds', 'allocationBytes', 'peakRssBytes']} for stage, row in [('before', before), ('after', after)]}
        ratios = {key: medians['after'][key] / medians['before'][key] for key in medians['before']}
        ratios['processPeakRssBytes'] = max(s['peakRssBytes'] for s in after['samples']) / max(s['peakRssBytes'] for s in before['samples'])
        pairs.append(dict(case=before['case'], round=before['round'], medians=medians, ratios=ratios,
                          investigate=any(ratios[key] > 1.05 for key in ['milliseconds', 'peakRssBytes', 'processPeakRssBytes'])))
    (work / (prefix + 'summary.json')).write_text(json.dumps(dict(pairs=pairs, protocol='Sequential alternating processes; iteration 0 cold, two further warm-ups, three warm samples; inclusive times not additive; process RSS includes validation/digest work.'), indent=2) + '\n')


if __name__ == '__main__':
    main()
