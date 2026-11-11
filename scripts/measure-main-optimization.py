#!/usr/bin/env python3
"""Compare saved main runtimes; never build or regenerate input between paired trials."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def prepare(work, runtime):
    inputs = work / 'inputs'
    inputs.mkdir(parents=True, exist_ok=True)
    cases = []

    def case(name, source, **settings):
        target = inputs / source.name
        if not target.exists():
            shutil.copy2(source, target)
        cases.append(dict(Name=name, Input=str(target.resolve()), **settings))

    core = ROOT / 'docs/validation/core-performance-optimization/inputs'
    for name in ('text1', 'text1000', 'wrap1000', 'borders1000', 'imagesonly', 'header', 'empty', 'mixed1000'):
        case(name + '-Pdf', core / (name + '.xlsx'))
    samples = ROOT / 'tests/ExcelRenderer.Tests/SampleInputs'
    for name in ('02-image', '04-text-decoration', '05-borders'):
        for form in ('Png', 'Svg'):
            case(name + '-' + form, samples / (name + '.xlsx'), Format=form)
    retained = ROOT / 'tests/Shared/RetainedWorkbooks'
    for kind, filename in (
        ('rich', 'RichTextAcceptanceTests.RangeTrimAndMergedCellRetainRichTextInAllOutputFormats.79F50EFE1F4F079D.xlsx'),
        ('shapes', 'ShapeAndFontTests.CreateWorkbookWithShapes.C704163BF33ECFBD.xlsx'),
    ):
        for form in ('Pdf', 'Png', 'Svg'):
            case(kind + '-' + form, retained / filename, Format=form, Trim=True)
    range_source = inputs / 'range-source.xlsx'
    if not range_source.exists():
        subprocess.run(['dotnet', str(runtime.resolve() / 'ExcelRenderer.Performance.dll'), 'generate', str(range_source), '20', 'text'], cwd=ROOT, check=True)
    case('range-trim-Pdf', range_source, Range='A5:F15', Trim=True)
    case('selected-Pdf', core / 'text1000.xlsx', Pages=[1])
    (work / 'cases.json').write_text(json.dumps(cases, indent=2) + '\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--before', type=Path, required=True)
    parser.add_argument('--after', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--prepare', action='store_true')
    parser.add_argument('--diagnostics', action='store_true')
    parser.add_argument('--rounds', type=int, default=3)
    parser.add_argument('--case', action='append')
    args = parser.parse_args()
    work = args.output.resolve()
    work.mkdir(parents=True, exist_ok=True)
    if args.prepare:
        prepare(work, args.before)
        return
    runtimes = dict(before=args.before.resolve(), after=args.after.resolve())
    environment = dict(
        os=platform.platform(), cpu=platform.processor(), affinity=sorted(os.sched_getaffinity(0)),
        sdk=subprocess.check_output(['dotnet', '--info'], text=True),
        cpuInfo=Path('/proc/cpuinfo').read_text().split('\n\n')[0],
        runtimeVariables={key: value for key, value in os.environ.items() if key.startswith(('DOTNET_', 'COMPlus_'))},
        inputs={path.name: sha(path) for path in (work / 'inputs').glob('*.xlsx')},
        fonts={str(path.relative_to(ROOT)): sha(path) for directory in ('third_party/NotoSansJP', 'tests/ExcelRenderer.Tests/Fonts') for path in (ROOT / directory).rglob('*') if path.suffix in ('.ttf', '.otf', '.ttc')},
        dlls={stage: {path.name: sha(path) for path in runtime.glob('*.dll')} for stage, runtime in runtimes.items()},
        protocol='Saved Release runtimes; sequential processes; alternating order per round; observer-free primary; cold and warm separate. Inclusive phase times are not additive.',
    )
    (work / ('diagnostic-environment.json' if args.diagnostics else 'environment.json')).write_text(json.dumps(environment, indent=2) + '\n')
    command = ['python3', str(ROOT / 'scripts/benchmark-render-finalization.py'), '--before', str(runtimes['before']), '--after', str(runtimes['after']), '--output', str(work), '--rounds', str(args.rounds)]
    if args.diagnostics:
        command.append('--diagnostics')
    for case in args.case or []:
        command.extend(['--case', case])
    subprocess.run(command, cwd=ROOT, check=True)
    if args.diagnostics:
        return
    rows = []
    output = work / 'layout-samples.jsonl'
    output.write_text('')
    for round_index in range(args.rounds):
        for workload in ('plain', 'plain-wrap', 'rich-wrap'):
            for stage in ('before', 'after') if round_index % 2 == 0 else ('after', 'before'):
                command = ['dotnet', str(runtimes[stage] / 'ExcelRenderer.Performance.dll'), 'layout-case', workload, '5']
                result = subprocess.check_output(command, cwd=ROOT, text=True)
                samples = [json.loads(line) for line in result.splitlines()]
                record = dict(round=round_index, stage=stage, workload=workload, command=command, samples=samples)
                rows.append(record)
                with output.open('a') as stream:
                    stream.write(json.dumps(record) + '\n')
    summary = []
    for before in (row for row in rows if row['stage'] == 'before'):
        after = next(row for row in rows if row['stage'] == 'after' and row['round'] == before['round'] and row['workload'] == before['workload'])
        a, b = before['samples'][-1], after['samples'][-1]
        assert a['InputSha256'] == b['InputSha256'] and a['FontSha256'] == b['FontSha256']
        assert a['OutputSha256'] == b['OutputSha256'], (before['workload'], 'layout/run/glyph/cluster digest differs')
        summary.append(dict(round=before['round'], workload=before['workload'], layoutEqual=True,
                            ratios={key: b[key] / a[key] for key in ('TotalMs', 'AllocatedBytes', 'PeakWorkingSet64')}))
    (work / 'layout-summary.json').write_text(json.dumps(summary, indent=2) + '\n')


if __name__ == '__main__':
    main()
