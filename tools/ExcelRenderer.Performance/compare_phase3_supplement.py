#!/usr/bin/env python3
"""Measure page selection, repeated images, font loading, input spool, and ten-run retention."""
import argparse
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--baseline', type=Path, required=True)
parser.add_argument('--current', type=Path, default=Path.cwd())
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
harness = Path('tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll')
conditions = [('pdf', 'paginated'), ('png', 'paginated'), ('svg', 'paginated'), ('png', 'continuous'), ('svg', 'continuous')]

def execute(root, arguments):
    result = subprocess.run(['dotnet', str(root / harness), *map(str, arguments)], cwd=root,
                            capture_output=True, text=True, check=True, timeout=300)
    return [json.loads(line) for line in result.stdout.splitlines() if line.startswith('{')]

def measure(case, arguments, repeats=3):
    for label, root in [('baseline', args.baseline), ('phase3', args.current)]:
        for run in range(repeats):
            try:
                records = execute(root, arguments)
            except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
                records = [dict(Error=str(error))]
            with (args.output / 'supplement.jsonl').open('a') as output:
                for record in records:
                    record.update(Case=case, Revision=label, Run=run)
                    output.write(json.dumps(record) + '\n')
            print(case, label, run, flush=True)

for rows, mode in [(100, 'latin'), (1000, 'images'), (100, 'mixed')]:
    execute(args.current, ['generate', args.output.resolve() / f'{mode}-{rows}.xlsx', rows, mode])
fixture = args.output.resolve() / 'latin-100.xlsx'
images = args.output.resolve() / 'images-1000.xlsx'
mixed = args.output.resolve() / 'mixed-100.xlsx'
large = args.output.resolve() / 'latin-10000.xlsx'
for format_, layout in conditions:
    measure('images-' + format_ + '-' + layout, ['render-case', images, format_, layout, 1])
    measure('retention-' + format_ + '-' + layout, ['render-case', fixture, format_, layout, 10], repeats=1)
measure('selection-one-of-many', ['render-case', large, 'pdf', 'paginated', 1, 1])
measure('system-fonts', ['render-case', fixture, 'pdf', 'paginated', 1, '--system-fonts'])
measure('font-pack', ['render-case', fixture, 'pdf', 'paginated', 1, '--font-pack'])
measure('mixed-print-areas-links', ['render-case', mixed, 'pdf', 'paginated', 1])
spool = args.output.resolve() / 'input-32MiB.zip'
execute(args.current, ['input-spool', spool, 'generate'])
measure('input-spool', ['input-spool', spool])
