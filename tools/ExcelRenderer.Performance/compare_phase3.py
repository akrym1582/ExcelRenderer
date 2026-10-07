#!/usr/bin/env python3
"""Run identical Release harnesses serially in fresh processes; retain raw JSONL evidence."""
import argparse
import json
from pathlib import Path
import statistics
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--baseline', type=Path, required=True)
parser.add_argument('--current', type=Path, default=Path.cwd())
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--rows', type=int, nargs='+', default=[1000, 5000, 10000])
parser.add_argument('--modes', nargs='+', default=['latin', 'empty'])
parser.add_argument('--repeats', type=int, default=3)
parser.add_argument('--revisions', nargs='+', choices=['baseline', 'phase3'], default=['baseline', 'phase3'])
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
harness = Path('tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll')
conditions = [('pdf', 'paginated'), ('png', 'paginated'), ('svg', 'paginated'), ('png', 'continuous'), ('svg', 'continuous')]
summary = []
for rows in args.rows:
    for mode in args.modes:
        fixture = args.output.resolve() / f'{mode}-{rows}.xlsx'
        if not fixture.exists():
            subprocess.run(['dotnet', str(args.current / harness), 'generate', str(fixture), str(rows), mode], cwd=args.current, check=True)
        for format_, layout in conditions:
            results = {}
            for label, root in [('baseline', args.baseline), ('phase3', args.current)]:
                if label not in args.revisions:
                    continue
                measurements = []
                for run in range(args.repeats):
                    try:
                        completed = subprocess.run(['dotnet', str(root / harness), 'render-case', str(fixture), format_, layout, '1'], cwd=root, text=True, capture_output=True, check=True, timeout=300)
                    except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
                        failure = dict(Rows=rows, FixtureMode=mode, Format=format_, Layout=layout, Revision=label, Run=run, Error=str(error))
                        with (args.output / 'failures.jsonl').open('a') as output:
                            output.write(json.dumps(failure) + '\n')
                        print(json.dumps(failure), flush=True)
                        break
                    raw = json.loads(completed.stdout.strip())
                    raw.update(Rows=rows, FixtureMode=mode, Revision=label, Run=run)
                    with (args.output / 'raw.jsonl').open('a') as output:
                        output.write(json.dumps(raw) + '\n')
                    measurements.append(raw)
                results[label] = {key: statistics.median(item[key] for item in measurements) for key in ['TotalMs', 'PeakWorkingSet64', 'PrivateMemorySize64', 'ManagedBytes', 'AllocatedBytes', 'Pages', 'OutputBytes']} if len(measurements) == args.repeats else None
            item = dict(Rows=rows, FixtureMode=mode, Format=format_, Layout=layout, Results=results)
            summary.append(item)
            (args.output / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
            print(json.dumps(item), flush=True)
