#!/usr/bin/env python3
"""Verify that four deliberate PDF/anchor regressions are detected, then restore sources."""
import pathlib
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[1]
PROJECT = 'tests/ExcelRenderer.Slim.Tests/ExcelRenderer.Slim.Tests.csproj'
OUT = ROOT / 'TestResults/Slim/Mutations'
CASES = [
    ('baseline', 'Pdf/PdfSharpFinalizedTextPainter.cs',
     'new XPoint(positioned.Left, positioned.Baseline)', 'new XPoint(positioned.Left, positioned.Baseline + 3)',
     'Finalized_baseline_and_underline'),
    ('underline', 'Pdf/PdfSharpFinalizedTextPainter.cs',
     'positioned.Baseline + 1', 'positioned.Baseline + 4', 'Finalized_baseline_and_underline'),
    ('image-page', 'Layout/RenderPageBuilder.cs',
     'sourceBounds.Y < vertical.Start', 'sourceBounds.Y <= vertical.Start', 'Image_is_drawn_only'),
    ('emu', 'Excel/DrawingMLReader.cs',
     'emu / EmusPerPoint', 'emu / (EmusPerPoint * 2)', 'DrawingML_anchors_preserve'),
]


def test(filter_text=None):
    command = ['dotnet', 'test', PROJECT, '-c', 'Release', '--no-restore']
    if filter_text:
        command += ['--filter', filter_text]
    return subprocess.run(command, cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    baseline = test()
    (OUT / 'baseline.log').write_text(baseline.stdout)
    if baseline.returncode:
        raise RuntimeError('Clean tests failed before mutation')
    for name, relative, old, new, filter_text in CASES:
        path = ROOT / 'src/ExcelRenderer.Slim' / relative
        original = path.read_text()
        if old not in original:
            raise RuntimeError(f'Mutation anchor absent: {name}')
        try:
            path.write_text(original.replace(old, new))
            result = test(filter_text)
            (OUT / f'{name}.log').write_text(result.stdout)
            if result.returncode == 0 or '[FAIL]' not in result.stdout:
                raise RuntimeError(f'Mutation was not caught by a failing test: {name}')
            print(f'Detected: {name}', flush=True)
        finally:
            path.write_text(original)
    final = test()
    (OUT / 'restored.log').write_text(final.stdout)
    if final.returncode:
        raise RuntimeError('Tests failed after source restoration')


if __name__ == '__main__':
    main()
