"""Independent structural evidence for individually reviewed PDF raster exceptions.

This module never changes raster thresholds. Missing/moved glyphs, altered fonts,
or path geometry cannot be accepted merely because an exception key exists.
"""
from functools import lru_cache
from pathlib import Path
import difflib
import io
import math
import re

import fitz
from fontTools.ttLib import TTFont
from fontTools.pens.qu2cuPen import Qu2CuPen
from fontTools.pens.recordingPen import DecomposingRecordingPen, RecordingPen

ROOT = Path(__file__).resolve().parents[1]
FONTS = {"NotoSansJP-Regular": ROOT / "third_party/NotoSansJP/NotoSansJP-Regular.ttf",
         "IPAmjMincho": ROOT / "third_party/IPAmjMincho/ipamjm.ttf"}


@lru_cache(maxsize=1)
def emoji_font():
    return TTFont(ROOT / "third_party/NotoColorEmoji/NotoColorEmoji.ttf")


def family(name):
    return name.replace(" ", "").replace("-", "").lower()


@lru_cache(maxsize=4)
def source_font(name):
    font = TTFont(FONTS[name])
    return font, font.getGlyphSet()


def source_edges(glyph):
    name = next(name for name in FONTS if family(name) == family(glyph["font"]))
    font, glyphs = source_font(name)
    pen = RecordingPen()
    glyphs[font.getGlyphOrder()[glyph["gid"]]].draw(Qu2CuPen(pen, 1e-6, all_cubic=True))
    origin, scale = glyph["origin"], glyph["size"] / font["head"].unitsPerEm
    def point(value):
        return (origin[0] + value[0] * scale, origin[1] - value[1] * scale)
    edges = []
    current = start = None
    for operation, args in pen.value:
        if operation == "moveTo":
            current = start = point(args[0])
        elif operation in ("lineTo", "curveTo"):
            transformed = tuple(point(value) for value in args)
            edges.append(("l" if operation == "lineTo" else "c", current, *transformed))
            current = transformed[-1]
        elif operation == "closePath" and current != start:
            edges.append(("l", current, start))
            current = start
    return edges


def path_error(expected, actual):
    if len(expected) != len(actual):
        return math.inf
    largest = 0
    for reference, observed in zip(expected, actual):
        if reference[0] != observed[0] or len(reference) != len(observed):
            return math.inf
        for a, b in zip(reference[1:], observed[1:]):
            if not hasattr(b, "x"):
                return math.inf
            largest = max(largest, abs(a[0] - b.x), abs(a[1] - b.y))
    return largest


def coordinates(value):
    if isinstance(value, (fitz.Point, fitz.Rect, fitz.Quad)):
        return [coordinates(part) for part in value]
    if isinstance(value, (list, tuple)):
        return [coordinates(part) for part in value]
    return value


def values_equal(a, b, tolerance):
    if isinstance(a, (list, tuple)) and isinstance(b, (list, tuple)):
        return len(a) == len(b) and all(values_equal(x, y, tolerance) for x, y in zip(a, b))
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return abs(a - b) <= tolerance
    return a == b


def same_paint(left, right):
    for key in ("type", "closePath", "even_odd", "lineCap", "lineJoin"):
        if left.get(key) != right.get(key):
            return False
    for key in ("fill", "color", "width", "fill_opacity", "stroke_opacity"):
        if not values_equal(left.get(key), right.get(key), .001):
            return False
    dash = lambda drawing: [float(number) for number in re.findall(r"[-+]?(?:\d*\.)?\d+", drawing.get("dashes") or "")]
    a, b = coordinates(left["items"]), coordinates(right["items"])
    if len(a) == len(b) == 1 and a[0][0] == b[0][0] == "re":
        # A single rectangle has no winding-sensitive holes.
        a, b = a[0][:2], b[0][:2]
    return values_equal(a, b, .0015) and values_equal(dash(left), dash(right), .0015)


def glyphs(page):
    return [{"font": trace["font"], "gid": char[1], "unicode": char[0], "origin": char[2],
             "bbox": char[3], "size": trace["size"], "color": trace["color"], "opacity": trace["opacity"]}
            for trace in page.get_texttrace() for char in trace["chars"] if char[1] >= 0]


def verify_outlines(page):
    errors, count = [], 0
    for info in page.get_fonts(full=True):
        data = page.parent.extract_font(info[0])[3]
        if not data:
            continue
        embedded = TTFont(io.BytesIO(data))
        name = embedded["name"].getDebugName(6) if "name" in embedded else next((name for name in FONTS if family(info[3].split("+")[-1]) == family(name)), None)
        if name not in FONTS:
            errors.append("Unknown outline source: " + str(name)); continue
        original, source_glyphs = source_font(name)
        embedded_glyphs = embedded.getGlyphSet()
        used = {glyph["gid"] for glyph in glyphs(page) if family(glyph["font"]) == family(name)}
        for gid in used:
            try:
                before = DecomposingRecordingPen(source_glyphs)
                after = DecomposingRecordingPen(embedded_glyphs)
                source_glyphs[original.getGlyphOrder()[gid]].draw(before)
                embedded_glyphs[embedded.getGlyphOrder()[gid]].draw(after)
                if before.value != after.value:
                    errors.append(f"Changed source outline {name}:{gid}")
                count += 1
            except (IndexError, KeyError):
                errors.append(f"Missing source outline {name}:{gid}")
    return {"passed": not errors, "errors": errors, "usedOutlines": count}


def inspect_page(left, right):
    a, b = glyphs(left), glyphs(right)
    identity = lambda glyph: (family(glyph["font"]), glyph["gid"])
    matcher = difflib.SequenceMatcher(None, list(map(identity, a)), list(map(identity, b)), autojunk=False)
    matched, extra = [], []
    errors = []
    for operation, i, j, k, l in matcher.get_opcodes():
        if operation == "equal":
            matched.extend(zip(a[i:j], b[k:l]))
        elif operation == "insert":
            extra.extend(b[k:l])
        else:
            errors.append(f"Reference text glyphs removed or replaced: {operation} {i}:{j}/{k}:{l}")
    position_error = max((max(abs(x - y) for x, y in zip(x["origin"], y["origin"])) for x, y in matched), default=0)
    size_error = max((abs(x["size"] - y["size"]) for x, y in matched), default=0)
    color_error = max((max(abs(v - w) for v, w in zip(x["color"], y["color"])) for x, y in matched), default=0)
    # These limits only bound PDFsharp's serialized 3-decimal Tf precision and
    # accumulated advances; logical commands must separately match at generation.
    if position_error > .02 or size_error > .0006 or color_error > 1 / 255 or any(abs(x["opacity"] - y["opacity"]) > 1e-5 for x, y in matched):
        errors.append("Ordinary glyph geometry/style exceeds serialized precision bounds")
    paths = left.get_drawings()
    used = set()
    path_errors = []
    color_glyphs = []
    for glyph in extra:
        if glyph["font"] == "IPAmjMincho" or (family(glyph["font"]) == family("NotoSansJP-Regular") and glyph["gid"] == 0):
            edges = source_edges(glyph)
            choices = [(path_error(edges, drawing["items"]), index) for index, drawing in enumerate(paths) if index not in used and drawing["type"] in ("f", "fs")]
            error, index = min(choices, default=(math.inf, -1))
            if error > .002:
                errors.append(f"Unmatched source outline: glyph {glyph['gid']}, error {error}")
            else:
                used.add(index); path_errors.append(error)
        elif glyph["font"] == "Unnamed-T3":
            # The reference contains a 4x rasterized CBDT glyph. Native Skia uses
            # a canonical 64px-em Type3 charproc from that same fixed color face.
            images = [image for image in left.get_image_info() if fitz.Rect(image["bbox"]).contains(fitz.Point(glyph["origin"]))]
            if not images:
                errors.append(f"Color glyph has no matching reference image at baseline: U+{glyph['unicode']:X}")
            original = emoji_font()
            name = original.getBestCmap().get(glyph["unicode"])
            gid = original.getGlyphID(name) if name else None
            type3_faces = [font[0] for font in right.get_fonts(full=True) if font[2] == "Type3"]
            if gid is None or not any(right.parent.xref_get_key(face, f"CharProcs/g{gid:X}")[0] == "xref" for face in type3_faces):
                errors.append(f"Color glyph does not reference the fixed CBDT glyph: U+{glyph['unicode']:X}")
            color_glyphs.append({"unicode": glyph["unicode"], "glyph": glyph["gid"], "origin": glyph["origin"], "referenceImages": len(images)})
        else:
            errors.append(f"Unmatched text glyph: {glyph['font']} {glyph['gid']}")
    left_paints = [drawing for index, drawing in enumerate(paths) if index not in used]
    right_paints = right.get_drawings()
    paints_equal = len(left_paints) == len(right_paints) and all(same_paint(a, b) for a, b in zip(left_paints, right_paints))
    if not paints_equal:
        errors.append("Non-glyph vector paint geometry/style/count differs")
    return {"passed": not errors, "errors": errors, "matchedTextGlyphs": len(matched), "maxOriginDeltaPt": position_error,
            "maxFontSizeDeltaPt": size_error, "maxColorDelta": color_error, "sourceOutlinePaths": len(path_errors),
            "maxSourcePathControlPointDeltaPt": max(path_errors, default=0), "colorGlyphs": color_glyphs, "nonGlyphPaintsEqual": paints_equal}


def ellipse_evidence(left, right):
    # Independent fixture dimensions, not inferred from either PDF.
    rectangle = fitz.Rect(215, 20, 375, 85)
    result = []
    for page in (left, right):
        candidates = [drawing for drawing in page.get_drawings() if max(abs(a - b) for a, b in zip(drawing["rect"], rectangle)) < .002]
        residual = 0
        curves = 0
        for drawing in candidates:
            for item in drawing["items"]:
                if item[0] != "c":
                    return {"passed": False, "reason": "Ellipse contains a non-cubic edge"}
                curves += 1
                for index in range(101):
                    t = index / 100
                    weights = ((1 - t) ** 3, 3 * t * (1 - t) ** 2, 3 * t * t * (1 - t), t ** 3)
                    x = sum(w * p.x for w, p in zip(weights, item[1:]))
                    y = sum(w * p.y for w, p in zip(weights, item[1:]))
                    residual = max(residual, abs(((x - 295) / 80) ** 2 + ((y - 52.5) / 32.5) ** 2 - 1))
        result.append({"curves": curves, "maxNormalizedEllipseResidual": residual})
    return {"passed": all(row["curves"] >= 4 and row["maxNormalizedEllipseResidual"] < .0006 for row in result), "curves": result}


def image_crop_evidence(page):
    # Independent 32x32 source: pixel (x,y) is RGB (7x,7y,100).
    # The fixture crops [.2,.1,.1,.25] into [290,30,80,80]. Sample opaque
    # interior pixels, away from alpha transitions and all clipped edges.
    pixmap = page.get_pixmap(dpi=288, alpha=False)
    samples = []
    for fx in (.6, .75, .9):
        for fy in (.3, .7):
            x, y = int((290 + 80 * fx) * 4), int((30 + 80 * fy) * 4)
            actual_fx = (((x + .5) / 4) - 290) / 80
            actual_fy = (((y + .5) / 4) - 30) / 80
            expected = [round((6.4 + 22.4 * actual_fx - .5) * 7), round((3.2 + 20.8 * actual_fy - .5) * 7), 100]
            actual = list(pixmap.pixel(x, y))
            samples.append({"expected": expected, "actual": actual, "maxChannelDelta": max(abs(a - b) for a, b in zip(expected, actual))})
    return {"passed": all(sample["maxChannelDelta"] <= 4 for sample in samples), "samples": samples}


def verify_font_formats(directory):
    import subprocess
    results = []
    for name in ("static.ttf", "cff.otf", "variable.ttf", "collection.ttc", "colr.ttf", "colr-v1.ttf"):
        path = directory / (name + ".pdf")
        document = fitz.open(path)
        mupdf = document[0].get_text().strip()
        poppler = subprocess.check_output(["pdftotext", "-raw", str(path), "-"], text=True).strip()
        assert mupdf == poppler == "A B", (name, mupdf, poppler)
        pix = document[0].get_pixmap(dpi=144)
        import numpy as np
        pixels = np.frombuffer(pix.samples, np.uint8).reshape(pix.height, pix.width, pix.n)
        assert (pixels[:, :, :3] < 128).any(), name
        if name.startswith("colr"):
            assert ((pixels[:, :, 0] > 200) & (pixels[:, :, 1] < 50) & (pixels[:, :, 2] < 50)).any(), name
        results.append({"font": name, "mupdf": mupdf, "poppler": poppler, "colorPixels": name.startswith("colr")})
    return results
