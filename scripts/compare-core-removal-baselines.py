#!/usr/bin/env python3
"""Compare frozen Core output with independent readers and unchanged raster thresholds."""
from collections import Counter
import difflib
import hashlib
import json
from pathlib import Path
import subprocess
from zipfile import ZipFile
import xml.etree.ElementTree as ET

import fitz
from fontTools.ttLib import TTFont
import numpy as np
from scipy.ndimage import binary_dilation
from pdf_geometry_evidence import verify_outlines
from pdf_native_evidence import verify_frozen_baseline

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/pdfsharp-removal-current/core'
FONT = ROOT / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'


def missing_source_scalars(source):
    cmap = TTFont(FONT).getBestCmap()
    result = Counter()
    with ZipFile(source) as archive:
        for name in archive.namelist():
            if name == 'xl/sharedStrings.xml' or (name.startswith('xl/worksheets/') and name.endswith('.xml')):
                for element in ET.fromstring(archive.read(name)).iter():
                    if element.tag.endswith('}t'):
                        result.update(character for character in element.text or ''
                                      if ord(character) > 0xffff and ord(character) not in cmap
                                      and not 0xe0100 <= ord(character) <= 0xe01ef)
    return result


def text_evidence(before, after, missing):
    before, after = ''.join(before.split()), ''.join(after.split())
    improvements = []
    for kind, a, b, c, d in difflib.SequenceMatcher(None, before, after, autojunk=False).get_opcodes():
        if kind == 'equal':
            continue
        old, new = before[a:b], after[c:d]
        # Only this independently verified source-notdef defect may improve.
        # Original missing-glyph appearance remains required by raster checks.
        assert all(character == '\0' for character in old), (old, new)
        assert new and all(character in missing for character in new), (old, new)
        improvements.append({'old': old, 'sourceUnicode': new})
    for character, count in missing.items():
        assert after.count(character) == count, (character, count, after.count(character))
    return improvements


def main():
    verify_frozen_baseline()
    OUT.mkdir(parents=True, exist_ok=True)
    results = []
    reviews = json.loads((ROOT / 'tools/ExcelRenderer.PdfComparison/reviewed-core-differences.json').read_text())
    for name, source in [('invoice', 'samples/mapping/invoice/invoice.xlsx'), ('grid', 'samples/excel/japanese-grid-report.xlsx'),
                         ('scaling', 'tests/ExcelRenderer.Core.Tests/Baselines/Inputs/08-print-scaling.xlsx')]:
        output = OUT / (name + '.pdf')
        process = subprocess.run(['dotnet', str(ROOT / 'tools/ExcelRenderer.Core.Validation/bin/Release/net10.0/ExcelRenderer.Core.Validation.dll'),
                                  'render', source, str(output), str(FONT), '1'], cwd=ROOT, text=True, capture_output=True, check=True)
        (OUT / (name + '.json')).write_text(process.stdout)
        baseline = ROOT / 'tests/Shared/PdfRemovalBaseline/core' / (name + '.pdf')
        missing = missing_source_scalars(ROOT / source)
        old, new = fitz.open(baseline), fitz.open(output)
        assert len(old) == len(new)
        record = {'fixture': name, 'pages': len(new), 'oldBytes': baseline.stat().st_size, 'newBytes': output.stat().st_size,
                  'missingSourceGlyphs': dict(missing), 'raster': []}
        record['popplerUnicodeImprovements'] = text_evidence(
            subprocess.check_output(['pdftotext', str(baseline), '-'], text=True),
            subprocess.check_output(['pdftotext', str(output), '-'], text=True), missing)
        record['mupdfUnicodeImprovements'] = text_evidence(''.join(page.get_text() for page in old), ''.join(page.get_text() for page in new), missing)
        old_characters = [character for page in old for trace in page.get_texttrace() for character in trace['chars']]
        new_characters = [character for page in new for trace in page.get_texttrace() for character in trace['chars']]
        assert len(old_characters) == len(new_characters)
        assert [character[1] for character in old_characters] == [character[1] for character in new_characters]
        record['maxGlyphOriginDelta'] = max(max(abs(a - b) for a, b in zip(left[2], right[2])) for left, right in zip(old_characters, new_characters))
        assert record['maxGlyphOriginDelta'] <= .1
        for number, (left, right) in enumerate(zip(old, new), 1):
            before_paths, after_paths = left.get_drawings(), right.get_drawings()
            assert len(before_paths) == len(after_paths)
            for before, after in zip(before_paths, after_paths):
                assert before['type'] == after['type']
                assert max(abs(a - b) for a, b in zip(before['rect'], after['rect'])) <= .01
                assert abs((before['width'] or 0) - (after['width'] or 0)) <= .001
            outlines = verify_outlines(right)
            assert outlines['passed'], outlines
            for attribute in ('mediabox', 'cropbox'):
                assert max(abs(a - b) for a, b in zip(getattr(left, attribute), getattr(right, attribute))) <= .01
            for dpi in (144, 300):
                pixmaps = [page.get_pixmap(dpi=dpi, alpha=False) for page in (left, right)]
                assert (pixmaps[0].width, pixmaps[0].height) == (pixmaps[1].width, pixmaps[1].height)
                images = [np.frombuffer(pix.samples, dtype=np.uint8).reshape(pix.height, pix.width, 3) for pix in pixmaps]
                regions = [('page-ink', (0, 0, left.rect.width, left.rect.height))]
                regions += [(f'block-{index + 1}', block[:4]) for index, block in enumerate(left.get_text('blocks'))]
                for region, bounds in regions:
                    x0, y0, x1, y1 = [max(0, int(round(value * dpi / 72))) for value in bounds]
                    cropped = [image[y0:y1, x0:x1] for image in images]
                    ink = [np.max(255 - image, axis=2) >= 32 for image in cropped]
                    union = ink[0] | ink[1]
                    denominator = max(1, int(np.sum(union)))
                    changed = (np.max(np.abs(cropped[0].astype(int) - cropped[1].astype(int)), axis=2) > 16) & union
                    far = (ink[0] & ~binary_dilation(ink[1])) | (ink[1] & ~binary_dilation(ink[0]))
                    color, distant = float(np.sum(changed) / denominator), float(np.sum(far) / denominator)
                    reviewed = False
                    if color > .01 or distant > .001:
                        matching = [review for review in reviews if (review['fixture'], review['page'], review['dpi'], review['region']) == (name, number, dpi, region)]
                        assert len(matching) == 1, (name, number, dpi, region, color, distant)
                        review = matching[0]
                        assert review['baselineSha256'] == hashlib.sha256(baseline.read_bytes()).hexdigest()
                        assert review['bounds'] == list(bounds)
                        assert review['currentRegionPixelsSha256'] == hashlib.sha256(cropped[1].tobytes()).hexdigest()
                        assert distant <= .001, 'AA review cannot excuse distant added/missing ink'
                        reviewed = True
                    record['raster'].append({'page': number, 'dpi': dpi, 'region': region, 'changedUnionFraction': color, 'distantInkFraction': distant, 'reviewedAntialiasing': reviewed})
        results.append(record)
    (OUT / 'comparison.json').write_text(json.dumps(results, ensure_ascii=False, indent=2) + '\n')
    print('Three frozen Core fixtures passed; missing-glyph source Unicode restored without adding font fallback')


if __name__ == '__main__':
    main()
