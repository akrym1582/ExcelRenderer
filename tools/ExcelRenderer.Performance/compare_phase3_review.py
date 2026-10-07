#!/usr/bin/env python3
"""Compare the review baseline and refactor in isolated, equally instrumented Release builds."""
import argparse
import hashlib
import io
import json
import os
import platform
import shutil
from pathlib import Path
import statistics
import subprocess
import tarfile
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument('--baseline', default='800e1f7738dd0ee61110312ff3e7b048d056d1b1')
parser.add_argument('--current', default='HEAD')
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--large-only', action='store_true', help='Run only the 10,000-row continuous SVG comparison.')
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
output = args.output.resolve()
if (output / 'raw.jsonl').exists():
    parser.error('Use a new output directory to avoid mixing measurements.')
repository = Path(subprocess.check_output(['git', 'rev-parse', '--show-toplevel'], text=True).strip())
revisions = {label: subprocess.check_output(['git', 'rev-parse', revision], text=True).strip()
             for label, revision in [('before', args.baseline), ('after', args.current)]}
harness = Path('tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll')
conditions = [(1000, 'pdf', 'paginated'), (1000, 'png', 'paginated'), (1000, 'svg', 'paginated'),
              (1000, 'png', 'continuous'), (1000, 'svg', 'continuous'), (10000, 'svg', 'continuous')]
if args.large_only:
    conditions = conditions[-1:]
summary = []
with tempfile.TemporaryDirectory(prefix='excel-review-benchmark-') as temporary:
    roots = {}
    for label, revision in revisions.items():
        root = Path(temporary) / label
        root.mkdir()
        archive = subprocess.check_output(['git', 'archive', revision], cwd=repository)
        with tarfile.open(fileobj=io.BytesIO(archive)) as files:
            files.extractall(root, filter='data')
        # Test-only counters in both scratch builds. Production metrics and CLI stay unchanged.
        source = root / 'src/ExcelRenderer/Layout/TextMeasurePass.cs'
        original = source.read_text()
        anchor = '            if (context.TextMeasurer is ITextLayoutService layoutService)'
        assert original.count(anchor) == 1
        source.write_text(original.replace(anchor,
            '            ExcelRenderer.Rendering.ConversionMetrics.Report("reviewTextMeasureCalls", 1);\n\n' + anchor))
        source = root / 'src/ExcelRenderer/ExcelConverter.RenderAsync.cs'
        original = source.read_text()
        anchor = '                    result.Save(stream, false);'
        assert original.count(anchor) == 1
        source.write_text(original.replace(anchor,
            '                    ExcelRenderer.Rendering.ConversionMetrics.Report("reviewPdfDocumentPages", result.PageCount);\n' + anchor))
        with (output / (label + '-build.log')).open('w') as log:
            subprocess.run(['dotnet', 'restore', 'ExcelRenderer.slnx'], cwd=root, stdout=log, stderr=log, check=True)
            subprocess.run(['dotnet', 'build', 'tools/ExcelRenderer.Performance', '-c', 'Release', '--no-restore', '--verbosity:minimal'],
                           cwd=root, stdout=log, stderr=log, check=True)
        # Avoid storing unused platform-native assets twice on a small Linux temp volume.
        # A 10,000-row SVG buffer itself needs about 1.9 GiB of free disk space.
        if platform.system() == 'Linux' and platform.machine() == 'x86_64':
            runtimes = (root / harness).parent / 'runtimes'
            for runtime in runtimes.iterdir():
                if runtime.is_dir() and runtime.name != 'linux-x64':
                    shutil.rmtree(runtime)
        roots[label] = root
    environment = dict(Revisions=revisions, SDK=subprocess.check_output(['dotnet', '--version'], text=True).strip(),
                       Fontconfig=os.environ.get('FONTCONFIG_FILE'), Instrumentation='Scratch-only text-measure calls and PDF document page count',
                       Repeats=3, Dpi=24, Columns=20, FixtureMode='latin')
    (output / 'environment.json').write_text(json.dumps(environment, indent=2) + '\n')
    for rows, format_, layout in conditions:
        fixture = output / f'latin-{rows}.xlsx'
        if not fixture.exists():
            subprocess.run(['dotnet', str(roots['after'] / harness), 'generate', str(fixture), str(rows), 'latin'],
                           cwd=roots['after'], check=True, capture_output=True)
        records = {'before': [], 'after': []}
        for run in range(3):
            for label in (['before', 'after'] if run % 2 == 0 else ['after', 'before']):
                try:
                    result = subprocess.run(['dotnet', str(roots[label] / harness), 'render-case', str(fixture), format_, layout, '1'],
                                            cwd=roots[label], text=True, capture_output=True, check=True, timeout=300)
                except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
                    failure = dict(Rows=rows, Format=format_, Layout=layout, Revision=label, Run=run,
                                   Error=str(error),
                                   Stdout=error.stdout.decode(errors='replace') if isinstance(error.stdout, bytes) else error.stdout,
                                   Stderr=error.stderr.decode(errors='replace') if isinstance(error.stderr, bytes) else error.stderr)
                    (output / 'failure.json').write_text(json.dumps(failure, indent=2) + '\n')
                    raise
                record = json.loads(result.stdout.strip())
                record.update(Rows=rows, Revision=label, Run=run, FixtureSHA256=hashlib.sha256(fixture.read_bytes()).hexdigest())
                records[label].append(record)
                with (output / 'raw.jsonl').open('a') as raw:
                    raw.write(json.dumps(record) + '\n')
                print(json.dumps(dict(Rows=rows, Format=format_, Layout=layout, Revision=label, Run=run,
                                     TotalMs=record['TotalMs'], PeakWorkingSet64=record['PeakWorkingSet64'])), flush=True)
        for items in records.values():
            for item in items:
                metrics = item['Metrics']
                assert metrics['pagePayloadMax'] == 1
                assert metrics.get('svgBufferMemoryPeakBytes', 0) <= 8 * 1024 * 1024
                if format_ == 'pdf':
                    assert metrics['pdfSave'] == 1
                    assert metrics['reviewPdfDocumentPages'] == item['Pages']
        assert {item['Pages'] for item in records['before']} == {item['Pages'] for item in records['after']}
        assert {item['Metrics']['reviewTextMeasureCalls'] for item in records['before']} == {
            item['Metrics']['reviewTextMeasureCalls'] for item in records['after']}
        results = {}
        for label, items in records.items():
            results[label] = {}
            for key in ['TotalMs', 'PeakWorkingSet64', 'AllocatedBytes', 'Pages', 'OutputBytes']:
                values = [item[key] for item in items]
                results[label][key] = dict(Median=statistics.median(values), Min=min(values), Max=max(values))
            results[label]['Metrics'] = {name: [item['Metrics'].get(name, 0) for item in items] for name in
                ['pagePayloadMax', 'reviewTextMeasureCalls', 'svgBufferMemoryPeakBytes', 'svg.spillCount', 'pdfSave', 'reviewPdfDocumentPages']}
        summary.append(dict(Rows=rows, Format=format_, Layout=layout, Results=results))
        (output / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
