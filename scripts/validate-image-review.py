#!/usr/bin/env python3
"""Independently check single-copy image payloads, xrefs and corrected image appearance."""
import argparse
import hashlib
import json
import struct
import zlib
from pathlib import Path
import fitz

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/pr90-image-review/independent.json')
    args = parser.parse_args()
    fixtures = ROOT / 'tests/ExcelRenderer.Core.Tests/bin/Release/net10.0/SampleOutputs/ImageReview'
    rows = []
    for name in ('rgb', 'alpha', 'jpeg', 'shared', 'multiple'):
        raw_path, final_path = (fixtures / (name + suffix + '.pdf') for suffix in ('-raw', '-final'))
        raw, final = fitz.open(raw_path), fitz.open(final_path)
        raw_bytes, final_bytes = raw_path.read_bytes(), final_path.read_bytes()
        assert len(raw) == len(final)
        assert len(final_bytes) <= len(raw_bytes) + 65536, name
        assert final.xref_get_key(-1, 'Prev')[0] == 'null', name
        raw_streams = {i: raw.xref_stream_raw(i) for i in range(1, raw.xref_length()) if raw.xref_is_stream(i)}
        final_streams = {i: final.xref_stream_raw(i) for i in range(1, final.xref_length()) if final.xref_is_stream(i)}
        assert raw_streams == final_streams, name
        masks = {int(raw.xref_get_key(i, 'SMask')[1].split()[0]) for i in raw_streams
                 if raw.xref_get_key(i, 'SMask')[0] == 'xref'}
        images = []
        for i, payload in raw_streams.items():
            if raw.xref_get_key(i, 'Subtype')[1] != '/Image':
                continue
            count = final_bytes.count(payload)
            assert count == 1, (name, i, count)
            if i not in masks:
                assert final.xref_get_key(i, 'Interpolate') == ('bool', 'true'), (name, i)
            else:
                assert final.xref_get_key(i, 'Interpolate') == raw.xref_get_key(i, 'Interpolate')
            images.append({'xref': i, 'bytes': len(payload), 'sha256': hashlib.sha256(payload).hexdigest(), 'occurrences': count,
                           'mask': i in masks, 'filter': final.xref_get_key(i, 'Filter')[1]})
        # Reference the old correctly interpolated output, not the raw uncorrected render.
        pixels = []
        if name == 'rgb':
            baseline = json.loads((ROOT / 'tests/Shared/ImageReviewBaseline.json').read_text())
            assert images[0]['sha256'] == baseline['payloadSha256']
            for dpi, expected in baseline['correctedPixelHashes'].items():
                raster = final[0].get_pixmap(dpi=int(dpi), alpha=False)
                digest = hashlib.sha256(raster.samples).hexdigest()
                assert digest == expected, (name, dpi, digest, expected)
                pixels.append({'dpi': int(dpi), 'sha256': digest, 'identical': True})
        rows.append({'fixture': name, 'pages': len(final), 'rawBytes': len(raw_bytes), 'finalBytes': len(final_bytes),
                     'images': images, 'streamHashesPreserved': True, 'correctedReferencePixels': pixels})
    main_outputs = ROOT / 'tests/ExcelRenderer.Tests/bin/Release/net10.0/SampleOutputs/ImageReview'
    corrupt = (main_outputs / 'corrupt-idat.png').read_bytes()
    offset, chunk_count = 8, 0
    while offset < len(corrupt):
        length = struct.unpack('>I', corrupt[offset:offset + 4])[0]
        body = corrupt[offset + 4:offset + 8 + length]
        expected = struct.unpack('>I', corrupt[offset + 8 + length:offset + 12 + length])[0]
        assert zlib.crc32(body) == expected, offset
        chunk_count += 1
        offset += length + 12
    e2e = []
    for name in ('jpeg', 'png'):
        path = main_outputs / (name + '-e2e.pdf')
        document = fitz.open(path)
        images = [i for i in range(1, document.xref_length()) if document.xref_get_key(i, 'Subtype')[1] == '/Image']
        assert len(document) == 2 and len(images) == 1, name
        payload = document.xref_stream_raw(images[0])
        if name == 'jpeg':
            assert document.xref_get_key(images[0], 'Filter')[1] == '/DCTDecode'
            assert payload == (main_outputs / 'original.jpg').read_bytes()
        assert path.read_bytes().count(payload) == 1
        e2e.append({'fixture': name, 'pages': len(document), 'imageObjects': 1, 'bytes': path.stat().st_size,
                    'filter': document.xref_get_key(images[0], 'Filter')[1], 'payloadSha256': hashlib.sha256(payload).hexdigest()})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({'PyMuPDF': fitz.VersionBind, 'fixtures': rows, 'endToEnd': e2e, 'corruptPngCrcChunksVerified': chunk_count, 'decode': json.loads((main_outputs / 'decode-status.json').read_text())}, indent=2) + '\n')
    print('Five image fixtures: encoded streams, single-copy payloads, masks, sharing, xrefs and corrected appearance passed')


if __name__ == '__main__':
    main()
