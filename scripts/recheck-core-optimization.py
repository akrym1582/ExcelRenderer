#!/usr/bin/env python3
"""Investigate noisy short/untargeted timings with tiering disabled and more warm samples."""
import json
import os
import pathlib
import statistics
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUTPUT = ROOT/'docs/validation/core-performance-optimization'
RUNTIMES = ROOT/'artifacts/core-performance'
FONT = ROOT/'third_party/NotoSansJP/NotoSansJP-Regular.ttf'
CASES = ['text1', 'empty', 'header', 'imagesonly', 'borders1000', 'repeat1000', 'images1000', 'wrap1000']
PHASES = ['baseline', 'phase4', 'phase5']
raw = OUTPUT/'controlled-samples.jsonl'
raw.write_text('')
for round_index in range(3):
    for case in CASES:
        phases = PHASES if round_index % 2 == 0 else list(reversed(PHASES))
        for phase in phases:
            command = ['dotnet', RUNTIMES/phase/'ExcelRenderer.Core.Validation.dll', 'render',
                       OUTPUT/'inputs'/(case+'.xlsx'), RUNTIMES/phase/(case+'-controlled.pdf'), FONT, 9]
            environment = dict(os.environ, CORE_PERF_DIAGNOSTICS='0', DOTNET_TieredCompilation='0')
            value = subprocess.check_output(list(map(str, command)), cwd=ROOT, text=True, stderr=subprocess.STDOUT, env=environment)
            samples = json.loads(value.splitlines()[-1])
            record = {'round': round_index, 'case': case, 'phase': phase, 'command': list(map(str, command)),
                      'environment_overrides': {'CORE_PERF_DIAGNOSTICS': '0', 'DOTNET_TieredCompilation': '0'},
                      'cold_conversions': 1, 'warm_conversions': 8, 'samples': samples}
            with raw.open('a') as stream:
                stream.write(json.dumps(record)+'\n')
            print(round_index, case, phase, round(statistics.median(s['Milliseconds'] for s in samples[1:]), 2), flush=True)
