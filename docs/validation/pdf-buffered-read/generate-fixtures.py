#!/usr/bin/env python3
"""Generate deterministic classic-xref inputs; do not update historical fixtures."""
import pathlib
import random
import shutil
import struct
import sys
import zipfile
import zlib

root = pathlib.Path(sys.argv[1])
root.mkdir(parents=True, exist_ok=True)
for name, pages, extra, lines, image in [
    ('small', 1, 0, 1, False),
    ('objects', 128, 1024, 1, False),
    ('borders', 8, 0, 5000, False),
    ('image', 1, 0, 1, True),
]:
    objects = [b'', b'']
    kids = []
    for page in range(pages):
        number = len(objects) + 1
        kids.append(f'{number} 0 R')
        objects.append(f'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents {number+1} 0 R /Resources << >> >>'.encode())
        content = b'% endstream xref /Length 0 obj\n' + b'0 0 m 100 100 l S\n' * lines
        objects.append(b'<< /Length ' + str(len(content)).encode() + b' >>\nstream\n' + content + b'endstream')
    for index in range(extra):
        objects.append(b'<< /Title (metadata\\(x\\) (nested)) /A [1 -2.75 true null <FEFF> 3 0 R] /Na#6De /Value >>')
    if image:
        # Valid raw RGB image (9.72 MB), larger than the default 8 MiB threshold.
        payload = bytes(range(256)) * (1800 * 1800 * 3 // 256) + bytes(range(1800 * 1800 * 3 % 256))
        objects.append(b'<< /Type /XObject /Subtype /Image /Width 1800 /Height 1800 /BitsPerComponent 8 /ColorSpace /DeviceRGB /Length ' + str(len(payload)).encode() + b' >>\nstream\n' + payload + b'\nendstream')
    objects[0] = b'<< /Type /Catalog /Pages 2 0 R >>'
    objects[1] = f'<< /Type /Pages /Kids [{" ".join(kids)}] /Count {pages} >>'.encode()
    output = bytearray(b'%PDF-1.7\n')
    offsets = [0]
    for number, value in enumerate(objects, 1):
        offsets.append(len(output))
        output.extend(f'{number} 0 obj\n'.encode() + value + b'\nendobj\n')
    xref = len(output)
    output.extend(f'xref\n0 {len(offsets)}\n0000000000 65535 f \n'.encode())
    for offset in offsets[1:]:
        output.extend(f'{offset:010d} 00000 n \n'.encode())
    output.extend(f'trailer\n<< /Size {len(offsets)} /Root 1 0 R /ID [<01234567><89ABCDEF>] >>\nstartxref\n{xref}\n%%EOF\n'.encode())
    (root / f'{name}.pdf').write_bytes(output)

fixtures = pathlib.Path(__file__).resolve().parent / 'fixtures'
for name in ('small', 'objects', 'borders'):
    shutil.copyfile(fixtures / f'{name}.xlsx', root / f'{name}.xlsx')

# Replace the template's uniform image with deterministic RGB noise. Keep the
# small original ZIP metadata/template, avoiding a large checked-in binary.
def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))

random_bytes = random.Random(7241).randbytes(1800 * 1800 * 3)
scanlines = b''.join(b'\0' + random_bytes[row * 5400:(row + 1) * 5400] for row in range(1800))
png = (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 1800, 1800, 8, 2, 0, 0, 0))
       + chunk(b'IDAT', zlib.compress(scanlines)) + chunk(b'IEND', b''))
with zipfile.ZipFile(fixtures / 'image-template.xlsx') as source, zipfile.ZipFile(root / 'image.xlsx', 'w') as target:
    for info in source.infolist():
        target.writestr(info, png if '/media/' in info.filename else source.read(info.filename))
