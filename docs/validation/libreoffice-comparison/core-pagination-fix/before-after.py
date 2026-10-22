"""Run from the repository root after generating the separate before/after PDFs."""
import json, importlib.util
from pathlib import Path
import fitz, numpy as np
from PIL import Image, ImageChops
root = Path.cwd()
spec = importlib.util.spec_from_file_location('comparison', root / 'scripts/compare-libreoffice.py')
c = importlib.util.module_from_spec(spec)
spec.loader.exec_module(c)
manifest = json.loads(c.MANIFEST.read_text())
old = root / 'artifacts/libreoffice-comparison'
new = old / 'core-pagination-fix'
report = root / 'docs/validation/libreoffice-comparison/core-pagination-fix'
records = []
for engine in ['main', 'core']:
    for case in manifest['cases']:
        a = old / 'actual' / engine / (case['id'] + '.pdf')
        b = new / 'actual' / engine / a.name
        with fitz.open(a) as x, fitz.open(b) as y:
            item = {'case_id': case['id'], 'engine': engine, 'before_sha256': c.sha(a), 'after_sha256': c.sha(b), 'before_pages': len(x), 'after_pages': len(y), 'text': c.text_difference(''.join((p.get_text() for p in x)), ''.join((p.get_text() for p in y))), 'pages': []}
            for i in range(max(len(x), len(y))):
                left = c.raster(x[i], 144) if i < len(x) else None
                right = c.raster(y[i], 144) if i < len(y) else None
                size = (max((p.width for p in [left, right] if p)), max((p.height for p in [left, right] if p)))
                canvases = []
                for image in [left, right]:
                    canvas = Image.new('RGB', size, 'white')
                    if image:
                        canvas.paste(image, (0, 0))
                    canvases.append(canvas)
                changed = np.any(np.asarray(ImageChops.difference(*canvases)) > 0, axis=2)
                item['pages'].append({'page': i + 1, 'before_present': left is not None, 'after_present': right is not None, 'before_size_pt': list(x[i].rect) if i < len(x) else None, 'after_size_pt': list(y[i].rect) if i < len(y) else None, 'changed_pixel_ratio_144dpi': float(changed.mean())})
            item['all_pages_pixel_identical'] = len(x) == len(y) and all((p['changed_pixel_ratio_144dpi'] == 0 and p['before_size_pt'] == p['after_size_pt'] for p in item['pages']))
            records.append(item)
summary = {}
for engine in ['main', 'core']:
    items = [x for x in records if x['engine'] == engine]
    summary[engine] = {'cases': len(items), 'before_pages': sum((x['before_pages'] for x in items)), 'after_pages': sum((x['after_pages'] for x in items)), 'pixel_identical_cases': sum((x['all_pages_pixel_identical'] for x in items)), 'changed_page_count_cases': [x['case_id'] for x in items if x['before_pages'] != x['after_pages']]}
assert summary['main']['pixel_identical_cases'] == 22, summary
assert summary['core']['after_pages'] == 31, summary
assert set(summary['core']['changed_page_count_cases']) == {'01-japanese', '02-image', '08-print-scaling'}, summary
c.save(report / 'before-after.json', {'note': 'Ordinal pages are not semantic correspondences when counts differ. Original before and after PDFs/images are retained separately.', 'summary': summary, 'records': records})
print(json.dumps(summary, indent=2))
