#!/usr/bin/env python3
"""Compare two CLI revisions: PDF text/page geometry and complete Markdown artifacts for tracked fixtures."""
import argparse
import hashlib
import json
import pathlib
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--baseline-cli', required=True, type=pathlib.Path, help='Path to a separately built baseline ExcelRenderer.Tool.dll.')
parser.add_argument('--fixture', action='append', help='Limit comparison to these tracked XLSX paths; repeatable.')
parser.add_argument('--work', required=True, type=pathlib.Path)
parser.add_argument('--output', required=True, type=pathlib.Path)
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[1]
cli = root / 'src/ExcelRenderer.Tool/bin/Release/net10.0/ExcelRenderer.Tool.dll'
font = root / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'
tracked = subprocess.check_output(['git', 'ls-files', '*.xlsx'], cwd=root, text=True).splitlines()
if args.fixture:
    assert set(args.fixture) <= set(tracked), 'Every selected fixture must be a tracked XLSX path.'
    tracked = args.fixture
results = []
for index, relative in enumerate(tracked):
    fixture = root / relative
    artifacts = {}
    for label, executable in [('baseline', args.baseline_cli.resolve()), ('current', cli)]:
        directory = args.work.resolve() / str(index) / label
        directory.mkdir(parents=True, exist_ok=True)
        for generated in directory.rglob("*"):
            if generated.is_file():
                generated.unlink()
        for command, name in [('pdf', 'report.pdf'), ('md', 'report.md')]:
            subprocess.run(['dotnet', str(executable), command, str(fixture), '-o', str(directory / name), '--formula-mode', 'cached', '--font-file', str(font), '--no-system-fonts'], cwd=root, capture_output=True, text=True, check=True)
        text = subprocess.check_output(['pdftotext', '-layout', '-enc', 'UTF-8', str(directory / 'report.pdf'), '-'])
        info = subprocess.check_output(['pdfinfo', str(directory / 'report.pdf')], text=True)
        geometry = [line for line in info.splitlines() if line.startswith(('Pages:', 'Page size:'))]
        markdown = {str(path.relative_to(directory)): hashlib.sha256(path.read_bytes()).hexdigest() for path in directory.rglob('*') if path.is_file() and path.suffix != '.pdf'}
        artifacts[label] = {'textSha256': hashlib.sha256(text).hexdigest(), 'geometry': geometry, 'markdownArtifacts': markdown}
    assert artifacts['baseline'] == artifacts['current'], (relative, artifacts)
    results.append({'fixture': relative, 'comparison': artifacts['current'], 'equal': True})
    print(f'{relative}: PDF text/pages/size and Markdown/image artifacts equal', flush=True)
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(results, ensure_ascii=False, indent=2) + '\n')
