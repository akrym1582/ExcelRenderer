#!/usr/bin/env python3
"""Independent fontTools + MuPDF validation of exported managed subsets/full-font PDF pairs."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import subprocess

import fitz
import numpy as np
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]


def checksum(data):
    data += bytes((-len(data)) % 4)
    return sum(struct.unpack(f'>{len(data) // 4}I', data)) & 0xffffffff


def sfnt(path):
    data = path.read_bytes()
    assert checksum(data) == 0xB1B0AFBA, path
    count = struct.unpack_from('>H', data, 4)[0]
    for index in range(count):
        tag, expected, offset, length = struct.unpack_from('>4sIII', data, 12 + index * 16)
        assert offset % 4 == 0, (path, tag)
        table = bytearray(data[offset:offset + length])
        if tag == b'head':
            table[8:12] = bytes(4)
        assert checksum(table) == expected, (path, tag)
        assert not any(data[offset + length:offset + ((length + 3) & ~3)]), (path, tag, 'padding')
    font = TTFont(path, checkChecksums=2)
    # Force independent readers to parse retained character/name/metric structures.
    for tag in font.keys():
        if tag != 'GlyphOrder':
            font[tag]
    return {'file': path.name, 'sha256': hashlib.sha256(data).hexdigest(),
            'bytes': len(data), 'glyphCount': font['maxp'].numGlyphs,
            'tables': {tag: record.length for tag, record in font.reader.tables.items()}}


def compare_pair(before, after, evidence, dpis=(144, 300)):
    rows = []
    with fitz.open(before) as full, fitz.open(after) as subset:
        assert len(full) == len(subset)
        for index in range(len(full)):
            a, b = full[index], subset[index]
            assert a.rect == b.rect and a.mediabox == b.mediabox
            assert a.get_text() == b.get_text(), (after, index, 'Unicode')
            words_a, words_b = a.get_text('words'), b.get_text('words')
            assert words_a == words_b, (after, index, 'text positions')
            for dpi in dpis:
                # Check every page at 144dpi; representative pages at 300dpi for
                # repeated 50/200-page workloads, explicitly recorded in results.
                if dpi == 300 and len(full) > 10 and index not in {0, len(full)//2, len(full)-1}:
                    continue
                x = a.get_pixmap(dpi=dpi, alpha=False)
                y = b.get_pixmap(dpi=dpi, alpha=False)
                assert (x.width, x.height, x.n) == (y.width, y.height, y.n)
                ax = np.frombuffer(x.samples, dtype=np.uint8)
                ay = np.frombuffer(y.samples, dtype=np.uint8)
                changed = int(np.count_nonzero(ax != ay))
                if changed:
                    x.save(evidence / f'{after.stem}-{index+1}-{dpi}-full.png')
                    y.save(evidence / f'{after.stem}-{index+1}-{dpi}-subset.png')
                assert changed == 0, (after, index, dpi, changed)
                rows.append({'page': index+1, 'dpi': dpi, 'changedChannels': changed})
        return {'name': after.stem, 'pages': len(full), 'fullBytes': before.stat().st_size,
                'subsetBytes': after.stat().st_size, 'unicodeAndPositionsEqual': True,
                'fontsPerPage': [len(page.get_fonts()) for page in subset], 'raster': rows}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--baseline', type=Path, help='Original Skia commands comparison output from the starting commit')
    args = parser.parse_args()
    directory = args.evidence.resolve()
    results = {'fontToolsVersion': __import__('fontTools').__version__, 'mupdfVersion': fitz.VersionBind,
               'sfnt': [sfnt(path) for path in sorted(directory.glob('*.ttf'))], 'pdf': [], 'baseline': []}
    for path in sorted(directory.glob('*-subset.pdf')):
        results['pdf'].append(compare_pair(path.with_name(path.name.replace('-subset.pdf', '-full.pdf')), path, directory))
    for path in sorted(directory.glob('*.pdf')):
        extracted = subprocess.check_output(['pdftotext', '-enc', 'UTF-8', str(path), '-'], text=True)
        if path.name.startswith('ivs-'):
            assert '辻\U000E0102辻\U000E0103葛\U000E0102' in extracted, (path, extracted)
        if path.name.startswith('color-'):
            assert '😀' in extracted and '日本語' in extracted and '\U000E0102' in extracted, (path, extracted)
    # Verify actual colored pixels, rather than only checking branch counters.
    for name in ['color-subset.pdf']:
        with fitz.open(directory / name) as document:
            pix = document[0].get_pixmap(dpi=144)
            pixels = np.frombuffer(pix.samples, dtype=np.uint8).reshape(-1, 3).astype(np.int16)
            colored = int(np.count_nonzero(pixels.max(axis=1) - pixels.min(axis=1) > 20))
            assert colored > 100, (name, colored)
            results['colorPixels144dpi'] = colored
    # Existing API tests export standalone COLR and color TTC PDFs. Requiring
    # independently rendered colored pixels catches missing layer/paint GIDs.
    test_output = ROOT / 'tests/ExcelRenderer.Tests/bin/Release/net10.0/SampleOutputs'
    color_paths = [test_output / 'PdfFormats' / name for name in ['colr.ttf.pdf', 'colr-v1.ttf.pdf']]
    color_paths += [test_output / 'ManagedTrueType' / f'collection-{index}.pdf' for index in [3, 4]]
    results['colorFormats'] = []
    for path in color_paths:
        if not path.is_file():
            raise RuntimeError(f'Run PdfBackendTests and FontCollectionTests before independent validation: {path}')
        with fitz.open(path) as document:
            pix = document[0].get_pixmap(dpi=144, alpha=False)
            pixels = np.frombuffer(pix.samples, dtype=np.uint8).reshape(-1, 3).astype(np.int16)
            colored = int(np.count_nonzero(pixels.max(axis=1) - pixels.min(axis=1) > 20))
            assert colored > 100, (path, colored)
            results['colorFormats'].append({'file': path.name, 'pages': len(document), 'coloredPixels144dpi': colored, 'bytes': path.stat().st_size})
    if args.baseline:
        for index in range(1, 5):
            before = args.baseline / f'commands-{index}' / 'skiasharp.pdf'
            after = directory / 'commands' / f'commands-{index}' / 'skiasharp.pdf'
            results['baseline'].append(compare_pair(before, after, directory))
    (directory / 'independent-validation.json').write_text(json.dumps(results, indent=2) + '\n')
    print(json.dumps({'sfntFiles': len(results['sfnt']), 'pdfPairs': len(results['pdf']),
                      'baselinePairs': len(results['baseline']), 'colorPixels144dpi': results['colorPixels144dpi']}))


if __name__ == '__main__':
    main()
