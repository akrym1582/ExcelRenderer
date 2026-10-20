#!/usr/bin/env python3
"""Compare same-format saved-runtime captures with independent rasterizers and text readers."""
import argparse
import importlib.util
import json
import subprocess
from pathlib import Path

import fitz
import numpy as np
from PIL import Image


def load(filename):
    spec = importlib.util.spec_from_file_location(filename.replace('-', '_'), Path(__file__).with_name(filename + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def compare(directory):
    ps_validator = load('validate-postscript')
    ps_decoder = load('decode-postscript-text')
    records = []
    failures = []
    before = directory / 'captures/before'
    after = directory / 'captures/after'
    for case in sorted(before.iterdir()):
        for source in sorted(case.iterdir()):
            target = after / case.name / source.name
            record = dict(case=case.name, file=source.name, raster=[])
            try:
                assert target.exists()
                suffix = source.suffix
                if suffix == '.png':
                    pairs = [(np.asarray(Image.open(source).convert('RGB')), np.asarray(Image.open(target).convert('RGB')), 72)]
                elif suffix == '.ps':
                    record['fontValidation'] = [ps_validator.validate(path) for path in (source, target)]
                    assert ps_decoder.decode(source) == ps_decoder.decode(target), 'PS text/glyph/cluster/geometry metadata differs'
                    def ps_pairs():
                        for dpi in (144, 300):
                            rasters = []
                            for stage, path in [('before', source), ('after', target)]:
                                output = directory / 'raster' / case.name / stage / str(dpi)
                                output.mkdir(parents=True, exist_ok=True)
                                subprocess.run(['gs', '-q', '-dSAFER', '-dBATCH', '-dNOPAUSE', '-sDEVICE=png16m', f'-r{dpi}', '-sOutputFile=' + str(output / 'page-%03d.png'), str(path)], check=True, capture_output=True)
                                rasters.append(sorted(output.glob('page-*.png')))
                            assert len(rasters[0]) == len(rasters[1])
                            for a, b in zip(*rasters):
                                yield np.asarray(Image.open(a).convert('RGB')), np.asarray(Image.open(b).convert('RGB')), dpi
                    pairs = ps_pairs()
                else:
                    documents = [fitz.open(path) if suffix == '.pdf' else fitz.open('pdf', fitz.open(path).convert_to_pdf()) for path in (source, target)]
                    old, new = documents
                    assert len(old) == len(new), 'Page count differs'
                    record['pages'] = len(old)
                    def document_pairs():
                        for a, b in zip(old, new):
                            assert list(a.rect) == list(b.rect), 'Page dimensions differ'
                            if suffix == '.pdf':
                                assert a.get_text() == b.get_text(), 'MuPDF Unicode differs'
                                assert a.get_texttrace() == b.get_texttrace(), 'Glyph IDs/origins differ'
                                def links(page):
                                    return [{key: value for key, value in link.items() if key not in ('xref', 'id')} for link in page.get_links()]
                                assert links(a) == links(b), 'Links differ'
                            for dpi in (144, 300):
                                pixels = [page.get_pixmap(dpi=dpi, alpha=False) for page in (a, b)]
                                yield tuple(np.frombuffer(pixel.samples, np.uint8).reshape(pixel.height, pixel.width, 3) for pixel in pixels) + (dpi,)
                    pairs = document_pairs()
                    if suffix == '.pdf':
                        text = [subprocess.check_output(['pdftotext', str(path), '-']) for path in (source, target)]
                        assert text[0] == text[1], 'Poppler Unicode differs'
                for a, b, dpi in pairs:
                    assert a.shape == b.shape, 'Raster dimensions differ'
                    count = 0
                    maximum = 0
                    for row in range(0, a.shape[0], 128):
                        left, right = a[row:row + 128], b[row:row + 128]
                        count += int(np.any(left != right, axis=2).sum())
                        maximum = max(maximum, int(np.max(np.abs(left.astype(np.int16) - right.astype(np.int16)))))
                    record['raster'].append(dict(dpi=dpi, width=a.shape[1], height=a.shape[0], changedPixels=count, maximumChannelDifference=maximum))
                    assert count == 0, 'Same-format pixels differ'
                record['passed'] = True
            except Exception as error:
                record.update(passed=False, error=str(error))
                failures.append(dict(case=case.name, file=source.name, error=str(error)))
            records.append(record)
    report = dict(records=records, failures=failures)
    (directory / 'quality.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(dict(compared=len(records), failures=failures), indent=2))
    return bool(failures)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    raise SystemExit(compare(parser.parse_args().directory))
