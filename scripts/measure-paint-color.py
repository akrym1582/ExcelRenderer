#!/usr/bin/env python3
"""Measure saved Paint-color runtimes with no overlapping builds, tests or rendering."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import statistics
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--controlled', action='store_true')
    args = parser.parse_args()
    work = args.directory.resolve()
    environment = os.environ.copy()
    environment.pop('CORE_PERF_DIAGNOSTICS', None)
    if args.controlled:
        environment['DOTNET_TieredCompilation'] = '0'
    output = work / ('controlled' if args.controlled else 'primary')
    output.mkdir(exist_ok=True)
    retained = ROOT / 'docs/validation/main-performance-optimization'
    cases = json.loads((retained / 'cases.json').read_text())
    (output / 'inputs').mkdir(exist_ok=True)
    for case in cases:
        target = output / 'inputs' / Path(case['Input']).name
        shutil.copy2(retained / 'inputs' / target.name, target)
        case['Input'] = str(target)
    (output / 'cases.json').write_text(json.dumps(cases, indent=2) + '\n')
    inputs = ROOT / 'docs/validation/core-performance-optimization/inputs'
    font = ROOT / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'
    runtime_hashes = {name: {p.name: sha(p) for p in (work / name).glob('*.dll')} for name in ['before-core', 'after-core', 'before-main', 'after-main']}
    metadata = dict(os=platform.platform(), cpu=Path('/proc/cpuinfo').read_text().split('\n\n')[0], affinity=sorted(os.sched_getaffinity(0)),
                    sdk=subprocess.check_output(['dotnet', '--info'], text=True),
                    settings={k: v for k, v in environment.items() if k.startswith(('DOTNET_', 'COMPlus_'))},
                    fontSha256=sha(font), inputs={p.name: sha(p) for p in inputs.glob('*.xlsx')}, runtimes=runtime_hashes,
                    protocol='Three alternating rounds; one cold iteration, two further warmups, three measured warm trials. Diagnostics off. Controlled disables tiering. Inclusive times are not additive; RSS is cumulative and includes untimed validation.')
    (output / 'environment.json').write_text(json.dumps(metadata, indent=2) + '\n')
    command = ['python3', str(ROOT / 'scripts/benchmark-render-finalization.py'), '--before', str(work / 'before-main'), '--after', str(work / 'after-main'), '--output', str(output)]
    names = [name + '-Pdf' for name in ['text1', 'text1000', 'wrap1000', 'borders1000', 'imagesonly', 'header', 'empty', 'rich']]
    for name in names + ['05-borders-Png', '05-borders-Svg']:
        command.extend(['--case', name])
    subprocess.run(command, cwd=ROOT, env=environment, check=True)
    rows = []
    raw = output / 'extra-samples.jsonl'
    raw.write_text('')
    for round_index in range(3):
        cases = ['micro-' + name for name in ['fill-repeat', 'fill-alternate', 'stroke-repeat', 'stroke-alternate']]
        for name in cases + ['text1', 'text1000', 'wrap1000', 'borders1000', 'empty', 'imagesonly', 'header']:
            for stage in ['before', 'after'] if round_index % 2 == 0 else ['after', 'before']:
                if name.startswith('micro-'):
                    command = ['dotnet', str(work / (stage + '-main') / 'ExcelRenderer.Performance.dll'), 'paint-color-case', name.removeprefix('micro-'), '300000']
                    samples = [json.loads(line) for line in subprocess.check_output(command, cwd=ROOT, env=environment, text=True).splitlines()]
                else:
                    command = ['dotnet', str(work / (stage + '-core') / 'ExcelRenderer.Core.Validation.dll'), 'render', str(inputs / (name + '.xlsx')), str(output / (name + '-' + stage + '.pdf')), str(font), '6']
                    samples = [dict(milliseconds=s['Milliseconds'], allocationBytes=s['AllocatedBytes'], peakRssBytes=s['PeakRss'], gc=s['GCCollections'], warmup=s['Iteration'] < 3, original=s) for s in json.loads(subprocess.check_output(command, cwd=ROOT, env=environment, text=True))]
                row = dict(round=round_index, case=name, stage=stage, command=command, samples=samples)
                rows.append(row)
                with raw.open('a') as stream:
                    stream.write(json.dumps(row) + '\n')
                print(round_index, name, stage, flush=True)
    pairs = []
    for before in (row for row in rows if row['stage'] == 'before'):
        after = next(row for row in rows if row['stage'] == 'after' and row['case'] == before['case'] and row['round'] == before['round'])
        if before['case'].startswith('micro-'):
            for a, b in zip(before['samples'], after['samples']):
                assert (a['inputSha256'], a['outputSha256']) == (b['inputSha256'], b['outputSha256'])
        medians = {stage: {key: statistics.median(s[key] for s in row['samples'] if not s['warmup']) for key in ['milliseconds', 'allocationBytes', 'peakRssBytes']} for stage, row in [('before', before), ('after', after)]}
        ratios = {key: medians['after'][key] / medians['before'][key] for key in medians['before']}
        ratios['processPeakRssBytes'] = max(s['peakRssBytes'] for s in after['samples']) / max(s['peakRssBytes'] for s in before['samples'])
        pairs.append(dict(case=before['case'], round=before['round'], medians=medians, ratios=ratios, investigate=any(ratios[k] > 1.05 for k in ['milliseconds', 'peakRssBytes', 'processPeakRssBytes'])))
    assert runtime_hashes == {name: {p.name: sha(p) for p in (work / name).glob('*.dll')} for name in runtime_hashes}
    (output / 'extra-summary.json').write_text(json.dumps(pairs, indent=2) + '\n')


if __name__ == '__main__':
    main()
