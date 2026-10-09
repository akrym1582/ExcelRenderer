#!/usr/bin/env python3
"""Prove that both products observe the same Core axis geometry, then restore it."""
import pathlib
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[1]
PATH = ROOT / 'src/ExcelRenderer.Core/Layout/SheetGeometry.cs'
OUT = ROOT / 'TestResults/Core/SharedMutations'
CASES = [
    ('Core', 'tests/ExcelRenderer.Core.Tests/ExcelRenderer.Core.Tests.csproj', 'Hidden_dimensions_and_origin_offsets_remain_in_geometry'),
    ('Full', 'tests/ExcelRenderer.Tests/ExcelRenderer.Tests.csproj', 'Sparse_geometry_preserves_boundaries'),
]


def run(label, project, filter_text):
    result = subprocess.run(['dotnet', 'test', project, '-c', 'Release', '--no-restore', '-m:1', '--filter', filter_text],
                            cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (OUT / (label + '.log')).write_text(result.stdout)
    return result


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    original = PATH.read_text()
    for label, project, filter_text in CASES:
        assert run(label + '-baseline', project, filter_text).returncode == 0
    anchor = 'return ((index - 1) * defaultSize) + differences[count];'
    assert original.count(anchor) == 1
    try:
        PATH.write_text(original.replace(anchor, 'return ((index - 1) * defaultSize) + differences[count] + 1;'))
        for label, project, filter_text in CASES:
            result = run(label + '-mutation', project, filter_text)
            assert result.returncode != 0 and '[FAIL]' in result.stdout, f'{label} did not detect the mutation'
            print(label + ': shared geometry mutation detected', flush=True)
    finally:
        PATH.write_text(original)
    for label, project, filter_text in CASES:
        assert run(label + '-restored', project, filter_text).returncode == 0
    print('Original geometry restored; both products passed', flush=True)


if __name__ == '__main__':
    main()
