#!/usr/bin/env python3
"""Create additional original MIT TTC fixtures, using existing test fonts; validation only (fonttools 4.61.1)."""
from pathlib import Path
from fontTools.ttLib import TTFont, TTCollection

ROOT = Path(__file__).resolve().parents[1] / 'tests/ExcelRenderer.Tests/Fonts/PdfFormats'


def load(name):
    font = TTFont(ROOT / name, recalcTimestamp=False)
    font.recalcTimestamp = False
    return font


def save(name, fonts, version=0x00010000):
    collection = TTCollection()
    collection.fonts = fonts
    collection.save(ROOT / name)
    if version == 0x00020000:
        # TTC v2 adds 12 zero bytes for the absent DSIG. Absolute table offsets
        # and face directory offsets must all move with the inserted descriptor.
        import struct
        data = bytearray((ROOT / name).read_bytes())
        count = struct.unpack_from('>I', data, 8)[0]
        end = 12 + count * 4
        data[end:end] = bytes(12)
        struct.pack_into('>I', data, 4, version)
        for index in range(count):
            position = 12 + index * 4
            offset = struct.unpack_from('>I', data, position)[0] + 12
            struct.pack_into('>I', data, position, offset)
            tables = struct.unpack_from('>H', data, offset + 4)[0]
            for table in range(tables):
                position = offset + 12 + table * 16 + 8
                struct.pack_into('>I', data, position, struct.unpack_from('>I', data, position)[0] + 12)
        (ROOT / name).write_bytes(data)


regular = load('static.ttf')
bold = load('static.ttf')
for glyph in bold.getGlyphOrder():
    advance, lsb = bold['hmtx'][glyph]
    bold['hmtx'][glyph] = (advance + 250, lsb)
bold['OS/2'].usWeightClass = 700
bold['OS/2'].fsSelection = 0x20
bold['head'].macStyle = 1
for platform, encoding, language in [(3, 1, 0x409), (1, 0, 0)]:
    bold['name'].setName('Bold', 2, platform, encoding, language)
    bold['name'].setName('PdfFormatFixture-Bold', 6, platform, encoding, language)
# Shared glyf, distinct metrics and character maps; B deliberately maps to A.
for table in bold['cmap'].tables:
    if table.isUnicode():
        table.cmap[66] = 'A'
save('collection-distinct.ttc', [regular, bold])
save('collection-v2.ttc', [regular, bold], 0x00020000)
save('collection-mixed.ttc', [regular, load('cff.otf'), load('variable.ttf'), load('colr.ttf'), load('colr-v1.ttf')])
