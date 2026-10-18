#!/usr/bin/env python3
"""Reproduce the tiny original MIT-licensed format fixtures; requires fonttools 4.61.1."""
from pathlib import Path
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.pens.t2CharStringPen import T2CharStringPen
from fontTools.ttLib import TTCollection, newTable
from fontTools.ttLib.tables._f_v_a_r import Axis
from fontTools.ttLib.tables.TupleVariation import TupleVariation
from fontTools.colorLib.builder import buildCOLR, buildCPAL

ROOT = Path(__file__).resolve().parents[1] / "tests/ExcelRenderer.Tests/Fonts/PdfFormats"
ROOT.mkdir(parents=True, exist_ok=True)
ORDER = [".notdef", "space", "A", "B", "layer"]


def make(cff=False):
    builder = FontBuilder(1000, isTTF=not cff)
    builder.setupGlyphOrder(ORDER)
    builder.setupCharacterMap({32: "space", 65: "A", 66: "B"})
    glyphs = {}
    for index, name in enumerate(ORDER):
        pen = T2CharStringPen(600, None) if cff else TTGlyphPen(None)
        if name != "space":
            pen.moveTo((80, 0)); pen.lineTo((520, 0)); pen.lineTo((300 if name == "B" else 520, 700)); pen.lineTo((80, 700)); pen.closePath()
        glyphs[name] = pen.getCharString() if cff else pen.glyph()
    if cff:
        builder.setupCFF("PdfFormatFixture", {"FullName": "PDF Format Fixture", "FamilyName": "PDF Format Fixture", "Weight": "Regular"}, glyphs, {})
    else:
        builder.setupGlyf(glyphs)
    builder.setupHorizontalMetrics({name: (600, 80) for name in ORDER})
    builder.setupHorizontalHeader(ascent=800, descent=-200)
    builder.setupNameTable({"familyName": "PDF Format Fixture", "styleName": "Regular", "uniqueFontIdentifier": "ExcelRendererSyntheticFormatFixture", "fullName": "PDF Format Fixture", "psName": "PdfFormatFixture", "copyright": "Original test shapes, MIT, ExcelRenderer contributors"})
    builder.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    builder.setupPost()
    builder.font["head"].created = builder.font["head"].modified = 3854131200
    builder.font.recalcTimestamp = False
    return builder.font


make(True).save(ROOT / "cff.otf")
make().save(ROOT / "static.ttf")
variable = make()
fvar = newTable("fvar"); fvar.axes = []; fvar.instances = []
axis = Axis(); axis.axisTag = "wght"; axis.minValue = 100; axis.defaultValue = 400; axis.maxValue = 900; axis.flags = 0; axis.axisNameID = 256
fvar.axes.append(axis); variable["fvar"] = fvar
gvar = newTable("gvar")
gvar.variations = {name: [TupleVariation({"wght": (0, 1, 1)}, [(0, 0)] * (len(variable["glyf"][name].coordinates) + 4))] for name in ORDER if name != "space"}
variable["gvar"] = gvar
variable["name"].setName("Weight", 256, 3, 1, 0x409)
variable.save(ROOT / "variable.ttf")
collection = TTCollection(); collection.fonts = [make(), make()]; collection.save(ROOT / "collection.ttc")
color = make(); color["COLR"] = buildCOLR({"A": [("layer", 0)]}); color["CPAL"] = buildCPAL([[(1, 0, 0, 1)]])
color.save(ROOT / "colr.ttf")
from fontTools.ttLib.tables.otTables import PaintFormat
color1 = make()
color1["COLR"] = buildCOLR({"A": {"Format": PaintFormat.PaintGlyph, "Glyph": "layer", "Paint": {"Format": PaintFormat.PaintSolid, "PaletteIndex": 0, "Alpha": 1.0}}}, version=1)
color1["CPAL"] = buildCPAL([[(1, 0, 0, 1)]])
color1.save(ROOT / "colr-v1.ttf")
