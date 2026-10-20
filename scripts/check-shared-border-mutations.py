#!/usr/bin/env python3
"""Prove fixed border contracts reject missing lines, missing text and extra ink."""
import argparse
import json
from pathlib import Path
import re

import fitz
from shared_border_evidence import evidence, raster_matches

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--baseline', type=Path, default=ROOT / 'artifacts/pdf-comparison/invoice')
parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/shared-border-mutations.json')
args = parser.parse_args()
before = fitz.open(args.baseline / 'baseline-skiasharp.pdf')
original = (args.baseline / 'skiasharp.pdf').read_bytes()
with fitz.open(stream=original, filetype='pdf') as current:
    review = evidence(before[0], current[0], 'full', 'invoice', 1)
    assert review['passed'], review
    assert raster_matches(review, 144, current[0].get_pixmap(dpi=144, alpha=False).samples)

mutations = {
    'missing-border': lambda content: re.sub(rb'(?m)^[-.\d]+ [-.\d]+ m\n[-.\d]+ [-.\d]+ l\nS\n', b'', content, count=1),
    'missing-text': lambda content: re.sub(rb'<[0-9A-Fa-f]+> Tj', b'<> Tj', content, count=1),
    'extra-ink': lambda content: content + b'\n0 0 0 rg\n20 20 10 10 re f\n',
}
results = []
for name, mutate in mutations.items():
    with fitz.open(stream=original, filetype='pdf') as changed:
        xref = changed[0].get_contents()[0]
        content = changed.xref_stream(xref)
        mutated = mutate(content)
        assert mutated != content, name + ': mutation site missing'
        changed.update_stream(xref, mutated)
        page = changed.reload_page(changed[0])
        review = evidence(before[0], page, 'full', 'invoice', 1)
        assert not review['passed'], name + ': changed PDF incorrectly accepted'
        assert not raster_matches(review, 144, page.get_pixmap(dpi=144, alpha=False).samples)
        results.append({'mutation': name, 'detected': True, 'errors': review['errors']})
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(results, indent=2) + '\n')
print('Shared-border contracts rejected missing border, missing text and extra ink')
