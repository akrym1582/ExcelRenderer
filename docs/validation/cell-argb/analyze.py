#!/usr/bin/env python3
"""Inspect retained outputs: pip install PyMuPDF==1.26.6 Pillow==12.3.0."""
from pathlib import Path
import hashlib
import json

import fitz
from PIL import Image

ROOT = Path(__file__).resolve().parent
ROWS = [('00', 2), ('01', 3), ('80', 4), ('FF', 5), ('six-digit RGB', 6)]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def pixels(image):
    result = []
    for prefix, row in ROWS:
        y = 36 + (row - 1) * 60  # top margin 18pt, row height 30pt, 144 DPI
        # Fixed interior fill / top border / font region, excluding labels.
        font = image.crop((254, y + 3, 460, y + 56)).convert('RGB').tobytes()
        result.append({
            'prefix': prefix,
            'fill_rgb': list(image.getpixel((594, y + 30)))[:3],
            'border_rgb': list(image.getpixel((822, y)))[:3],
            'dark_font_pixels': sum(max(font[i:i + 3]) < 64 for i in range(0, len(font), 3)),
        })
    return result


results = {}
for name in ['before', 'after', 'core-before', 'core-after', 'package-1.9.2']:
    path = ROOT / (name + '.pdf')
    with fitz.open(path) as pdf:
        page = pdf[0]
        image = page.get_pixmap(matrix=fitz.Matrix(2, 2))
        raster = Image.frombytes('RGB', (image.width, image.height), image.samples)
        results[name + '.pdf'] = {
            'sha256': digest(path),
            'pages': len(pdf),
            'text_alpha_values': sorted({span['alpha'] for block in page.get_text('dict')['blocks']
                                        if 'lines' in block for line in block['lines'] for span in line['spans']}),
            'rows': pixels(raster),
        }
for name in ['before', 'after']:
    path = ROOT / (name + '.png')
    with Image.open(path) as image:
        results[name + '.png'] = {'sha256': digest(path), 'size': list(image.size), 'rows': pixels(image)}
for name in ['after.pdf', 'core-after.pdf', 'after.png']:
    record = results[name]
    if name.endswith('.pdf'):
        assert record['text_alpha_values'] == [255], name
    for row in record['rows']:
        assert row['fill_rgb'] == [51, 102, 153], (name, row)
        assert row['border_rgb'] == [204, 51, 0], (name, row)
        assert row['dark_font_pixels'] > 100, (name, row)
(ROOT / 'measurements.json').write_text(json.dumps(results, indent=2) + '\n')
print('Verified opaque text, fill and borders in main PDF/PNG and Core PDF.')
