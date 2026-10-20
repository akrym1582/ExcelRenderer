"""Explicitly record reviewed border contracts; never invoked by validation/CI.

Run only after inspecting before/after PDFs and confirming border-only changes.
Requires the generated full and Core comparison outputs. Historical files are
read-only; this writes a separate contract rather than replacing old PDFs.
"""
from collections import Counter
import argparse
import json
from pathlib import Path
import sys

import fitz

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'scripts'))
from shared_border_evidence import CONTRACTS, independent_checks, non_border_commands, page_snapshot

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--core-output', type=Path, required=True)
args = parser.parse_args()
records = {'full': {}, 'core': {}}
for path in sorted((ROOT / 'artifacts/pdf-comparison').glob('*/expected.json')):
    name = path.parent.name
    frozen = ROOT / 'tests/Shared/PdfRemovalBaseline' / name / 'expected.json'
    old, new = json.loads(frozen.read_text()), json.loads(path.read_text())
    if old == new:
        continue
    assert [key for key in old if old[key] != new[key]] == ['commandFingerprints'], name
    for command_file in frozen.parent.glob('skiasharp-page-*-commands.jsonl'):
        before = command_file.read_text().splitlines()
        after = (path.parent / command_file.name).read_text().splitlines()
        assert non_border_commands(before) == non_border_commands(after), name
        # Existing independent lines also survive; only cell-border leaves may
        # be replaced. These fixtures have no nested independent line leaves.
        assert not Counter(row for row in before if row.startswith('DrawLineCommand')) - Counter(row for row in after if row.startswith('DrawLineCommand'))
    records['full'][name] = {'commandFingerprints': new['commandFingerprints'], 'pages': {}}
    with fitz.open(path.parent / 'baseline-skiasharp.pdf') as before, fitz.open(path.parent / 'skiasharp.pdf') as after:
        assert len(before) == len(after)
        for number, (left, right) in enumerate(zip(before, after), 1):
            assert not independent_checks(left, right), (name, number, independent_checks(left, right))
            records['full'][name]['pages'][str(number)] = {'before': page_snapshot(left), 'after': page_snapshot(right)}

for name in ('invoice', 'grid', 'scaling'):
    records['core'][name] = {'pages': {}}
    with fitz.open(ROOT / f'tests/Shared/PdfRemovalBaseline/core/{name}.pdf') as before, fitz.open(args.core_output / f'{name}.pdf') as after:
        assert len(before) == len(after)
        for number, (left, right) in enumerate(zip(before, after), 1):
            assert not independent_checks(left, right), (name, number, independent_checks(left, right))
            records['core'][name]['pages'][str(number)] = {'before': page_snapshot(left), 'after': page_snapshot(right)}

CONTRACTS.write_text(json.dumps(records, indent=2) + '\n')
print('Recorded exact border contracts for', {key: len(value) for key, value in records.items()})
