#!/usr/bin/env python3
"""Generate and verify all 44 reviewed product PDFs; never regenerate references."""
import argparse
import json
from pathlib import Path
import re
import subprocess

import fitz
from libreoffice_regression import BASE, ROOT, checked_baseline, compare, manifest


def mutation_checks(output, data):
    """Prove the same production gate rejects realistic PDF defects."""
    results = []
    samples = [
        ('missing-text', '01-japanese', lambda b: re.sub(rb'<[0-9A-Fa-f]+> Tj', b'<> Tj', b, count=1)),
        ('missing-border', 'border-conflicts', lambda b: re.sub(rb'(?m)^[-.\d]+ [-.\d]+ m\n[-.\d]+ [-.\d]+ l\nS\n', b'', b, count=1)),
        ('duplicate-border', 'border-conflicts', lambda b: re.sub(rb'(?m)^[-.\d]+ [-.\d]+ m\n[-.\d]+ [-.\d]+ l\nS\n', lambda match: match[0] + match[0], b, count=1)),
        ('extra-ink', '01-japanese', lambda b: b + b'\n0 0 0 rg\n20 20 10 10 re f\n'),
        ('shifted-content', '01-japanese', lambda b: b'1 0 0 1 3 0 cm\n' + b),
        ('wrong-source-text', '01-japanese', lambda b: b.replace(b'FEFF65E5', b'FEFF65E6', 1)),
        ('extra-page', '01-japanese', None),
    ]
    directory = output / 'mutations'
    directory.mkdir(parents=True, exist_ok=True)
    for name, case_id, mutate in samples:
        record = next(c for c in data['cases'] if c['id'] == case_id)['engines']['main']
        original = checked_baseline(record)
        candidate = directory / (name + '.pdf')
        with fitz.open(original) as document:
            if mutate is None:
                document.new_page()
            else:
                xref = document[0].get_contents()[0]
                content = document.xref_stream(xref)
                changed = mutate(content)
                if changed == content:
                    raise AssertionError(name + ': mutation site not found')
                document.update_stream(xref, changed)
            document.save(candidate)
        result = compare(original, candidate)
        if result['passed']:
            raise AssertionError(name + ': defect incorrectly accepted')
        results.append({'mutation': name, 'detected': True, 'errors': result['errors']})
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, default=ROOT / 'artifacts/libreoffice-regression')
    parser.add_argument('--no-generate', action='store_true', help='Verify PDFs generated in this same run directory.')
    args = parser.parse_args()
    output = args.run.resolve()
    output.mkdir(parents=True, exist_ok=True)
    data = manifest()
    if not args.no_generate:
        # Validate every input, approved PDF and reference before producing output.
        for case in data['cases']:
            for record in case['engines'].values():
                checked_baseline(record)
        subprocess.run(['python3', str(ROOT / 'scripts/compare-libreoffice.py'), 'render', '--run', str(output)], cwd=ROOT, check=True)
    report = {'contract': str(BASE.relative_to(ROOT)), 'records': [], 'mutation_checks': mutation_checks(output, data)}
    errors = []
    for case in data['cases']:
        for engine, record in case['engines'].items():
            expected = checked_baseline(record)
            actual = output / 'actual' / engine / (case['id'] + '.pdf')
            execution = actual.with_suffix('.run.json')
            if not actual.exists() or not execution.exists() or json.loads(execution.read_text())['status'] != 'generated':
                result = {'passed': False, 'errors': ['Output not generated successfully in this run']}
            else:
                result = compare(expected, actual, differences=output / 'differences' / engine / case['id'])
            result.update(case_id=case['id'], engine=engine)
            report['records'].append(result)
            errors.extend(f'{engine}/{case["id"]}: {error}' for error in result['errors'])
    report['errors'] = errors
    (output / 'regression-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print(f'{len(report["records"])} product PDFs; {len(errors)} failures; {len(report["mutation_checks"])} mutations detected')
    if errors:
        raise SystemExit('\n'.join(errors))


if __name__ == '__main__':
    main()
