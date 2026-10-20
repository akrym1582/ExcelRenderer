"""Review page 2 against pre-border main and the frozen PDF contract.

Usage: python docs/validation/core-raster-review/verify.py /path/to/pre-border.pdf
Run scripts/validate-core.py --visual-only first to generate current output.
"""
from collections import defaultdict
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile

import fitz
from PIL import Image, ImageChops, ImageStat

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'scripts'))
from shared_border_evidence import evidence, independent_checks, raster_matches

OUT = Path(__file__).resolve().parent
CURRENT = ROOT / 'TestResults/Core/Validation/08-print-scaling-core.pdf'
GOLDEN = ROOT / 'tests/ExcelRenderer.Core.Tests/Baselines/08-print-scaling-core-2.png'


def coverage(page):
    """Compare painted axial intervals, ignoring duplicates and subdivisions."""
    groups = defaultdict(list)
    for path in page.get_drawings():
        if path['type'] != 's':
            continue
        for kind, start, end in path['items']:
            assert kind == 'l'
            x0, y0, x1, y1 = [round(v, 3) for v in (*start, *end)]
            assert x0 == x1 or y0 == y1
            horizontal = y0 == y1
            key = (horizontal, y0 if horizontal else x0, round(path['width'], 3),
                   tuple(path['color']), path['dashes'], tuple(path['lineCap']))
            groups[key].append(sorted((x0, x1) if horizontal else (y0, y1)))
    result = {}
    for key, intervals in groups.items():
        merged = []
        for start, end in sorted(intervals):
            if merged and start <= merged[-1][1] + .001:
                merged[-1][1] = max(end, merged[-1][1])
            else:
                merged.append([start, end])
        result[key] = merged
    return result


before = fitz.open(sys.argv[1])
after = fitz.open(CURRENT)
frozen = fitz.open(ROOT / 'tests/Shared/PdfRemovalBaseline/core/scaling.pdf')
assert len(before) == len(after) == len(frozen) == 2
assert not independent_checks(before[1], after[1])
assert coverage(before[1]) == coverage(after[1])
review = evidence(frozen[1], after[1], 'core', 'scaling', 2)
assert review['passed'], review
for dpi in (144, 300):
    assert raster_matches(review, dpi, after[1].get_pixmap(dpi=dpi, alpha=False).samples)
with tempfile.TemporaryDirectory() as temporary:
    images = []
    for name, pdf in [('before', sys.argv[1]), ('after', CURRENT)]:
        prefix = str(Path(temporary) / name)
        subprocess.run(['pdftoppm', '-r', '96', '-f', '2', '-l', '2', '-png', str(pdf), prefix], check=True)
        images.append(Image.open(prefix + '-2.png').convert('RGB'))
    old, new = images
    assert hashlib.sha256(old.tobytes()).hexdigest() == '2053967c129f90903bdd5d983dc1cd721955544922e90106cc7d95d834cc5df3'
    delta = ImageChops.difference(old, new)
    golden_delta = ImageChops.difference(Image.open(GOLDEN).convert('RGB'), new)
    rows = {str(y): sum(delta.getpixel((x, y)) != (0, 0, 0) for x in range(delta.width)) for y in range(delta.height)}
    rows = {y: count for y, count in rows.items() if count}
    reference = Image.open(ROOT / 'TestResults/Core/Validation/08-print-scaling-reference-2.png').convert('RGB')
    assert ImageChops.difference(reference, new).getbbox() is None
    report = {
        'beforeCommit': '513ddd4d424d9afa6e92ed154abdc631841ba302',
        'afterCommit': '38390a69490fa56ca2ff28006d573eff5a3d925c',
        'poppler': subprocess.run(['pdftoppm', '-v'], capture_output=True, text=True).stderr.strip(),
        'changedPixelsFromPreBorder': sum(rows.values()), 'changedRows': rows,
        'preBorderMae': sum(ImageStat.Stat(delta).mean) / 3,
        'frozenGoldenMae': sum(ImageStat.Stat(golden_delta).mean) / 3,
        'currentPixelsSha256': hashlib.sha256(new.tobytes()).hexdigest(),
        'paintCounts': [len(page.get_drawings()) for page in (before[1], after[1])],
        'equalAxialCoverageAndStyles': True, 'unchangedNonBorderContent': True,
        'identicalCoreAndReferencePixels': True, 'existingPdfContract': review,
    }
    (OUT / 'evidence.json').write_text(json.dumps(report, indent=2) + '\n')
    # Top/right corner and the next row boundary: old, new, amplified delta.
    crop = (675, 110, 700, 145)
    panel = Image.new('RGB', (75, 35), 'white')
    for index, image in enumerate((old, new, delta.point(lambda value: min(255, value * 8)))):
        panel.paste(image.crop(crop), (25 * index, 0))
    panel.resize((600, 280), Image.Resampling.NEAREST).save(OUT / 'edge-comparison.png')
print(json.dumps(report, indent=2))
