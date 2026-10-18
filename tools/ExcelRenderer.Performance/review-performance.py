#!/usr/bin/env python3
"""Compare two current-product builds across conversion, unique text layout and direct image output."""
import argparse
import concurrent.futures
import hashlib
import json
from pathlib import Path
import platform
import statistics
import subprocess

ROOT = Path(__file__).resolve().parents[2]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--before', type=Path, required=True)
    parser.add_argument('--after', type=Path, required=True)
    parser.add_argument('--inputs', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--rounds', type=int, default=5)
    parser.add_argument('--only', help='Optional workload name prefix for focused investigation')
    args = parser.parse_args()
    if args.rounds < 5:
        parser.error('Use at least five fresh-process rounds')
    assemblies = {'before': args.before.resolve(), 'after': args.after.resolve()}
    if not all(path.is_file() for path in assemblies.values()):
        parser.error('Both harness assemblies must exist')
    args.output.mkdir(parents=True, exist_ok=True)
    conditions = {'os': platform.platform(), 'sdk': subprocess.check_output(['dotnet', '--version'], text=True).strip(),
                  'rounds': args.rounds, 'warmups': 3, 'order': 'alternating before/after by round',
                  'assemblies': {stage: {file.name: sha(file) for file in assembly.parent.glob('ExcelRenderer*.dll')}
                                 for stage, assembly in assemblies.items()}, 'inputs': {}}
    cases = []
    for name in ('invoice', 'grid', 'images', '50-pages', '200-pages', 'rich-text', 'borders'):
        path = {'invoice': ROOT / 'samples/mapping/invoice/invoice.xlsx',
                'grid': ROOT / 'samples/excel/japanese-grid-report.xlsx'}.get(name, args.inputs.resolve() / (name + '.xlsx'))
        if not path.is_file():
            parser.error(f'Missing existing input: {path}')
        conditions['inputs'][name] = {'path': str(path), 'sha256': sha(path)}
        for concurrency in ((1, 4, 8) if name == 'rich-text' else (1,)):
            cases.append((f'pdf-{name}', concurrency, ['render-case', str(path), 'pdf', 'paginated', '4', '--font-pack', '--measure-text'], 'TotalMs'))
    for name in ('plain', 'rich', 'rich-wrap'):
        cases.append((f'layout-{name}', 1, ['layout-case', name, '30'], 'TotalMs'))
    for name in ('png', 'svg'):
        cases.append((f'direct-{name}-text', 1, ['direct-images', '', name, 'text', '4'], 'TotalMs'))
    (args.output / 'conditions.json').write_text(json.dumps(conditions, indent=2) + '\n')
    raw, summary = [], []
    for name, concurrency, command, metric in cases:
        if args.only and not name.startswith(args.only):
            continue
        start = len(raw)
        for round_index in range(args.rounds):
            stages = list(assemblies) if round_index % 2 == 0 else list(reversed(assemblies))
            for stage in stages:
                def execute(worker):
                    invocation = command.copy()
                    if invocation[0] == 'direct-images':
                        invocation[1] = str((args.output / 'images' / name / stage / str(round_index) / str(worker)).resolve())
                    result = subprocess.run(['dotnet', str(assemblies[stage]), *invocation], cwd=ROOT, text=True, capture_output=True, check=True)
                    records = [json.loads(line) for line in result.stdout.splitlines() if line.startswith('{')]
                    measured = [record for record in records if metric in record][-1]
                    return {'workload': name, 'concurrency': concurrency, 'round': round_index, 'worker': worker, 'stage': stage, **measured}
                with concurrent.futures.ThreadPoolExecutor(max_workers=concurrency) as pool:
                    raw.extend(pool.map(execute, range(concurrency)))
                (args.output / 'raw.json').write_text(json.dumps(raw, indent=2) + '\n')
        values = raw[start:]
        # Reject changed text/style/geometry or direct-image bytes independently of time.
        if name.startswith('layout-'):
            for field in ('OutputSha256', 'InputSha256', 'FontSha256', 'Lines'):
                if len({record[field] for record in values}) != 1:
                    raise RuntimeError(f'Layout evidence differs: {name}, {field}')
        if name.startswith('direct-'):
            if len({json.dumps(record['Outputs'], sort_keys=True) for record in values}) != 1:
                raise RuntimeError(f'Image output bytes differ: {name}')
        if name.startswith('pdf-'):
            if len({record['Pages'] for record in values}) != 1:
                raise RuntimeError(f'Page counts differ: {name}')
        groups = {stage: {field: {'median': statistics.median(record[field] for record in values if record['stage'] == stage),
                                  'max': max(record[field] for record in values if record['stage'] == stage)}
                          for field in ('TotalMs', 'AllocatedBytes', 'PeakWorkingSet64')}
                  for stage in assemblies}
        ratios = {field: groups['after'][field]['median'] / groups['before'][field]['median']
                  for field in ('TotalMs', 'AllocatedBytes', 'PeakWorkingSet64')}
        summary.append({'workload': name, 'concurrency': concurrency, 'stages': groups, 'medianRatios': ratios})
        (args.output / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
        print(json.dumps(summary[-1]), flush=True)
    if not summary:
        parser.error('No workload selected')


if __name__ == '__main__':
    main()
