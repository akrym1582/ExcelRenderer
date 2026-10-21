#!/usr/bin/env python3
"""Compare actual Skia outputs independently with PyMuPDF; ignore timestamp metadata."""
import hashlib
import json
import pathlib
import sys
import fitz

root = pathlib.Path(sys.argv[1]).resolve()
evidence = pathlib.Path(__file__).resolve().parent
report = []
for name in ('small', 'objects', 'borders', 'image'):
    for threshold in ((8388608, 1, 67108864) if name == 'image' else (8388608, 1)):
        stages = {}
        for stage in 'ABC':
            path = root / f'{name}-xlsx-{stage}-{threshold}.final.pdf'
            with fitz.open(path) as document:
                pages = []
                images = {}
                for page in document:
                    links = [{k: str(v) for k, v in link.items() if k not in ('xref', 'id')}
                             for link in page.get_links()]
                    pages.append(dict(size=list(page.rect), text=page.get_text(), links=links,
                                      pixelsSha256=hashlib.sha256(page.get_pixmap(alpha=False).samples).hexdigest()))
                    for image in page.get_images(full=True):
                        for xref in image[:2]:
                            if xref and xref not in images:
                                images[xref] = hashlib.sha256(document.xref_stream_raw(xref)).hexdigest()
                stages[stage] = dict(pages=pages, imageStreams=images)
        assert stages['A'] == stages['B'] == stages['C'], (name, threshold)
        report.append(dict(case=name, threshold=threshold, equivalent=True, details=stages['C']))
(evidence / 'output-comparison.json').write_text(json.dumps(dict(reader='PyMuPDF ' + fitz.VersionBind, cases=report), ensure_ascii=False, indent=2) + '\n')
print('A/B/C page dimensions, links, extracted text, image streams and page pixels match.', flush=True)
