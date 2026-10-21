#!/usr/bin/env python3
"""Compare saved Release runtimes with Phase 3 fixtures, raw samples and assembly hashes."""
import argparse
import hashlib
import json
import os
import runpy
import shutil
import statistics
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--before', type=Path, required=True)
    parser.add_argument('--after', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--pin-cpu', help='Pin measured processes to an allowed CPU number, or auto for the first allowed CPU')
    parser.add_argument('--case', choices=('no-cf', 'disabled', 'phase2', 'bars', 'icons', 'shared'))
    parser.add_argument('--stable-jit', action='store_true', help='Disable tiered compilation equally in both measured runtimes')
    parser.add_argument('--fixture-source', type=Path)
    parser.add_argument('--rounds', type=int, default=3)
    args = parser.parse_args()
    work = args.output.resolve()
    work.mkdir(parents=True, exist_ok=True)
    assert not any(work.iterdir()), 'Use an empty evidence directory'
    for name in ('Directory.Build.props', 'Directory.Build.targets'):
        (work / name).write_text('<Project/>\n')
    fixture = runpy.run_path(str(ROOT / 'scripts/generate-conditional-formatting-phase3.py'))
    values = fixture['rows']([i % 101 for i in range(10000)])
    bar = fixture['legacy_bar']('A1:A10000')
    icon = fixture['icons']('A1:A10000', '3Arrows', 2)
    scale = '<conditionalFormatting sqref="A1:A10000"><cfRule type="colorScale" priority="1"><colorScale><cfvo type="min"/><cfvo type="max"/><color rgb="FF000000"/><color rgb="FFFFFFFF"/></colorScale></cfRule></conditionalFormatting>'
    cases = [('no-cf', '', 'enabled'), ('disabled', bar + icon, 'disabled'), ('phase2', scale, 'enabled'), ('bars', bar, 'enabled'), ('icons', icon, 'enabled'), ('shared', bar + icon, 'enabled')]
    if args.case:
        cases = [case for case in cases if case[0] == args.case]
    bench = runpy.run_path(str(ROOT / 'scripts/benchmark-conditional-formatting.py'))['bench']
    harness = bench.HARNESS
    # The parent module installs the conditional-formatting harness during import.
    bench.HARNESS = harness.replace('iteration < 15', 'iteration < 13').replace('iteration < 10', 'iteration < 3')
    runtimes = {'before': args.before.resolve(), 'after': args.after.resolve()}
    binaries = {stage: bench.build_harness(source, work / stage) for stage, source in runtimes.items()}
    assemblies = {stage: {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in source.glob('ExcelRenderer*.dll')} for stage, source in runtimes.items()}
    inputs = {}
    for name, rules, _ in cases:
        path = work / f'{name}.xlsx'
        if args.fixture_source:
            shutil.copyfile(args.fixture_source / path.name, path)
        else:
            fixture['write'](path, values, rules, columns=1)
        inputs[name] = hashlib.sha256(path.read_bytes()).hexdigest()
    if args.pin_cpu is not None:
        available = os.sched_getaffinity(0)
        chosen = min(available) if args.pin_cpu == 'auto' else int(args.pin_cpu)
        assert chosen in available, 'Requested CPU is outside the current affinity mask'
        os.sched_setaffinity(0, {chosen})
    runtime_environment = os.environ.copy()
    if args.stable_jit:
        runtime_environment['DOTNET_TieredCompilation'] = '0'
    records = []
    for round_index in range(args.rounds):
        for name, _, mode in cases:
            stages = ['before', 'after'] if name in ('no-cf', 'disabled', 'phase2') else ['after']
            if round_index % 2:
                stages.reverse()
            for stage in stages:
                result = subprocess.run(['dotnet', str(binaries[stage]), str(work / f'{name}.xlsx'), str(ROOT / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'), mode], capture_output=True, text=True, check=True, env=runtime_environment)
                samples = [json.loads(line) for line in result.stdout.splitlines()]
                assert len(samples) == 13 and sum(not row['warmup'] for row in samples) == 10
                records.extend(dict(round=round_index, fixture=name, stage=stage, **sample) for sample in samples)
                (work / 'samples.json').write_text(json.dumps(records, indent=2) + '\n')
                print(f'{round_index}: {name}/{stage}', flush=True)
    summary = []
    for name, _, _ in cases:
        for stage in ('before', 'after'):
            for round_index in range(args.rounds):
                selected = [row for row in records if row['fixture'] == name and row['stage'] == stage and row['round'] == round_index and not row['warmup']]
                if not selected:
                    continue
                metrics = selected[0]['metrics']
                if stage == 'after' and name in ('bars', 'icons', 'shared'):
                    assert metrics['conditionalFormatting.populations'] == 1
                    assert metrics.get('conditionalFormatting.sorts', 0) == 0
                summary.append(dict(fixture=name, stage=stage, round=round_index, medianMs=statistics.median(row['elapsedMs'] for row in selected), medianBytes=statistics.median(row['allocatedBytes'] for row in selected), metrics=metrics))
    (work / 'summary.json').write_text(json.dumps(dict(warmups=3, measurements=10, tieredCompilation=runtime_environment.get("DOTNET_TieredCompilation", "runtime default"), cpuAffinity=sorted(os.sched_getaffinity(0)) if hasattr(os, "sched_getaffinity") else None, assemblies=assemblies, inputs=inputs, rounds=summary), indent=2) + '\n')
    for name in (case[0] for case in cases if case[0] in ('no-cf', 'disabled', 'phase2')):
        ratios = []
        for round_index in range(args.rounds):
            before = next(row for row in summary if row['fixture'] == name and row['stage'] == 'before' and row['round'] == round_index)
            after = next(row for row in summary if row['fixture'] == name and row['stage'] == 'after' and row['round'] == round_index)
            ratios.append(dict(time=after['medianMs'] / before['medianMs'], allocation=after['medianBytes'] / before['medianBytes']))
        print(name, json.dumps(ratios))


if __name__ == '__main__':
    main()
