#!/usr/bin/env python3
"""Run saved Release runtimes in rotating rounds, serially, with separate diagnostics."""
import hashlib
import json
import pathlib
import shutil
import statistics
import subprocess
import sys

root = pathlib.Path(sys.argv[1]).resolve()
evidence = pathlib.Path(__file__).resolve().parent
raw = []
# The natural image spill and the deliberately forced spills are separate cases.
cases = [(name, ext, threshold) for ext in ('pdf', 'xlsx')
         for name in ('small', 'objects', 'borders', 'image')
         for threshold in ((8388608, 1, 67108864) if name == 'image' else (8388608, 1))]
for round_number, order in enumerate(('ABC', 'BCA', 'CAB')):
    for name, ext, threshold in cases:
        path = root / 'fixtures' / f'{name}.{ext}'
        for stage in order:
            command = ['dotnet', str(root / stage / 'ExcelRenderer.Performance.dll'),
                       'pdf-read', str(path), str(threshold), '5']
            result = subprocess.run(command, text=True, capture_output=True)
            if result.returncode:
                print(result.stderr, file=sys.stderr)
                result.check_returncode()
            rows = [json.loads(line) for line in result.stdout.splitlines()]
            raw.append(dict(round=round_number, stage=stage, case=name, kind=ext,
                            threshold=threshold, command=command, stderr=result.stderr, rows=rows))
        print(f'round={round_number} case={name}.{ext} threshold={threshold}', flush=True)
(evidence / 'samples.json').write_text(json.dumps(raw, indent=2) + '\n')

diagnostics = []
for name, ext, threshold in cases:
    path = root / 'fixtures' / f'{name}.{ext}'
    for stage in 'ABC':
        command = ['dotnet', str(root / stage / 'ExcelRenderer.Performance.dll'),
                   'pdf-read', str(path), str(threshold), '1', '--diagnostic']
        result = subprocess.run(command, check=True, text=True, capture_output=True)
        output = pathlib.Path(str(path) + '.final.pdf')
        digest = hashlib.sha256(output.read_bytes()).hexdigest()
        diagnostics.append(dict(stage=stage, case=name, kind=ext, threshold=threshold,
                                outputHash=digest, rows=[json.loads(line) for line in result.stdout.splitlines()]))
        shutil.copyfile(output, root / f'{name}-{ext}-{stage}-{threshold}.final.pdf')
(evidence / 'diagnostics.json').write_text(json.dumps(diagnostics, indent=2) + '\n')
summary = []
for round_number in range(3):
    for name, ext, threshold in cases:
        for stage in 'ABC':
            entry = next(r for r in raw if (r['round'], r['stage'], r['case'], r['kind'], r['threshold']) == (round_number, stage, name, ext, threshold))
            samples = [r for r in entry['rows'] if r['Kind'] == 'sample' and not r['Warmup']]
            summary.append(dict(round=round_number, stage=stage, case=name, kind=ext, threshold=threshold,
                                metrics={key: dict(median=statistics.median(s[key] for s in samples),
                                                   minimum=min(s[key] for s in samples), maximum=max(s[key] for s in samples))
                                         for key in ('ElapsedMs', 'AllocatedBytes', 'FinalSaveMs', 'OutputBytes', 'Spills')}))
(evidence / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
for name, _, threshold in (c for c in cases if c[1] == 'pdf'):
    assert len({d['outputHash'] for d in diagnostics if d['case'] == name and d['kind'] == 'pdf' and d['threshold'] == threshold}) == 1, (name, threshold)
print('A/B/C deterministic output hashes match for every PDF case.', flush=True)
