#!/usr/bin/env python3
"""Recheck the remaining >5% timing/RSS observations without hiding primary results."""
import json
import os
import pathlib
import statistics
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUTPUT = ROOT/'docs/validation/core-performance-optimization'
RUNTIMES = ROOT/'artifacts/core-performance'
FONT = ROOT/'third_party/NotoSansJP/NotoSansJP-Regular.ttf'
ENVIRONMENT = dict(os.environ, CORE_PERF_DIAGNOSTICS='0', DOTNET_TieredCompilation='0')
raw = OUTPUT/'regression-rechecks.jsonl'
raw.write_text('')

def record(value):
    with raw.open('a') as stream:
        stream.write(json.dumps(value)+'\n')

for ri in range(3):
    for case in ['text1000', 'text5000', 'text10000', 'mixed1000']:
        phases = ['baseline', 'phase5'] if ri % 2 == 0 else ['phase5', 'baseline']
        for phase in phases:
            command = ['dotnet', RUNTIMES/phase/'ExcelRenderer.Core.Validation.dll', 'render', OUTPUT/'inputs'/(case+'.xlsx'),
                       RUNTIMES/phase/(case+'-recheck.pdf'), FONT, 9]
            result = subprocess.check_output(list(map(str, command)), cwd=ROOT, text=True, stderr=subprocess.STDOUT, env=ENVIRONMENT)
            samples = json.loads(result.splitlines()[-1])
            record({'product': 'Core', 'round': ri, 'case': case, 'phase': phase, 'samples': samples,
                    'command': list(map(str, command)), 'tiered_compilation': False, 'warm_conversions': 8})
            print('Core', ri, case, phase, flush=True)

# Select every main case with an observed >5% time or RSS increase in any primary round.
primary = json.loads((OUTPUT/'main-samples.json').read_text())
flagged = []
for case, fmt in dict.fromkeys((entry['case'], entry['format']) for entry in primary):
    for ri in range(3):
        old = next(x for x in primary if x['round'] == ri and x['case'] == case and x['format'] == fmt and x['phase'] == 'baseline-main')['samples'][1:]
        new = next(x for x in primary if x['round'] == ri and x['case'] == case and x['format'] == fmt and x['phase'] == 'final-main')['samples'][1:]
        if any(statistics.median(x[key] for x in new) > statistics.median(x[key] for x in old)*1.05 for key in ['TotalMs', 'PeakWorkingSet64']):
            flagged.append((case, fmt))
            break
for ri in range(3):
    for case, fmt in flagged:
        phases = ['baseline-main', 'final-main'] if ri % 2 == 0 else ['final-main', 'baseline-main']
        for phase in phases:
            command = ['dotnet', RUNTIMES/phase/'ExcelRenderer.Performance.dll', 'render-case',
                       ROOT/'tests/ExcelRenderer.Tests/SampleInputs'/(case+'.xlsx'), fmt, 'Paginated', 9]
            result = subprocess.check_output(list(map(str, command)), cwd=ROOT, text=True, stderr=subprocess.STDOUT, env=ENVIRONMENT)
            samples = [json.loads(line) for line in result.splitlines() if line.startswith('{')]
            samples = [sample for sample in samples if 'TotalMs' in sample]
            assert len(samples) == 9
            record({'product': 'main', 'round': ri, 'case': case, 'format': fmt, 'phase': phase, 'samples': samples,
                    'command': list(map(str, command)), 'tiered_compilation': False, 'warm_conversions': 8})
            print('main', ri, case, fmt, phase, flush=True)
