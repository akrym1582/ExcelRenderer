#!/usr/bin/env python3
"""Compare saved no-wrap PDF/PNG/SVG captures using the existing structural gates."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    work = args.directory.resolve()
    spec = importlib.util.spec_from_file_location('validation', ROOT / 'scripts/validate-main-optimization.py')
    validation = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(validation)
    source = work / 'primary'
    subprocess.run(['python3', str(ROOT / 'scripts/benchmark-render-finalization.py'), '--before', str(work / 'before-main'), '--after', str(work / 'after-main'), '--output', str(source), '--diagnostics', '--warmups', '1'], cwd=ROOT, check=True)
    report = dict(main=[], core=[])
    for case in sorted((source / 'captures/before').iterdir()):
        for old in sorted(case.iterdir()):
            new = source / 'captures/after' / case.name / old.name
            result = validation.pdf(old, new) if old.suffix == '.pdf' else dict(canonicalBytesEqual=validation.artifact_hash(old) == validation.artifact_hash(new))
            assert result.get('canonicalBytesEqual', True), old
            report['main'].append(dict(case=case.name, file=old.name, **result))
    for name in ['text1', 'text1000', 'wrap1000', 'borders1000', 'empty', 'imagesonly', 'header']:
        report['core'].append(dict(case=name, **validation.pdf(source / (name + '-before.pdf'), source / (name + '-after.pdf'))))
    (work / 'quality.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({name: len(rows) for name, rows in report.items()}))


if __name__ == '__main__':
    main()
