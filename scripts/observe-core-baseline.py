#!/usr/bin/env python3
"""Run the isolated counter-instrumented baseline only for processing-count evidence."""
import hashlib
import json
import os
import pathlib
import subprocess

import fitz

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUTPUT = ROOT/'docs/validation/core-performance-optimization'
RUNTIME = ROOT/'artifacts/core-performance/baseline-instrumented'
FONT = ROOT/'third_party/NotoSansJP/NotoSansJP-Regular.ttf'


def verify(case):
    """Ensure the counter-only patch did not alter the measured original's output."""
    with fitz.open(ROOT/'artifacts/core-performance/baseline'/(case+'.pdf')) as original, fitz.open(RUNTIME/(case+'.pdf')) as observed:
        assert len(original) == len(observed), case
        fonts = []
        for document in [original, observed]:
            references = {font[0] for page in document for font in page.get_fonts(full=True)}
            fonts.append(sorted(hashlib.sha256(document.extract_font(xref)[3]).hexdigest() for xref in references))
        assert fonts[0] == fonts[1], (case, 'embedded font subsets')
        for a, b in zip(original, observed):
            assert a.rect == b.rect and a.get_text('rawdict') == b.get_text('rawdict'), case
            x, y = a.get_pixmap(dpi=96), b.get_pixmap(dpi=96)
            assert (x.width, x.height, x.samples) == (y.width, y.height, y.samples), case
        return {'pages': len(original), 'pixel_text_coordinates_and_subsets_equal': True}


def main():
    records = []
    for source in sorted((OUTPUT/'inputs').glob('*.xlsx')):
        command = ['dotnet', RUNTIME/'ExcelRenderer.Core.Validation.dll', 'render', source, RUNTIME/(source.stem+'.pdf'), FONT, 1]
        result = subprocess.check_output(list(map(str, command)), cwd=ROOT, text=True, stderr=subprocess.STDOUT,
                                         env=dict(os.environ, CORE_PERF_DIAGNOSTICS='1'))
        records.append({'case': source.stem, 'command': list(map(str, command)), 'sample': json.loads(result.splitlines()[-1])[0],
                        'input_sha256': hashlib.sha256(source.read_bytes()).hexdigest(), 'output_verification': verify(source.stem)})
        print(source.stem, 'observed', flush=True)
    (OUTPUT/'baseline-observations.json').write_text(json.dumps({
        'purpose': 'Counters only. Added observation calls affect diagnostic timing; these timings are excluded from all primary/controlled performance ratios.',
        'core_sha256': hashlib.sha256((RUNTIME/'ExcelRenderer.Core.dll').read_bytes()).hexdigest(),
        'patch_sha256': hashlib.sha256((OUTPUT/'baseline-diagnostics.patch').read_bytes()).hexdigest(),
        'records': records,
    }, indent=2)+'\n')


if __name__ == '__main__':
    main()
