#!/usr/bin/env python3
"""Generate and independently compare PDF backends. Nonzero exit means an unmet gate.

Validation dependencies: PyMuPDF==1.26.6, numpy==2.3.5, Pillow==12.3.0,
fonttools==4.61.1, scipy==1.17.0; Poppler pdftotext on PATH.
These are validation tools, never dependencies of PDF generation.
"""
import argparse
import html
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import importlib.metadata

import fitz
import numpy as np
from PIL import Image
from fontTools.ttLib import TTFont
from fontTools.pens.recordingPen import DecomposingRecordingPen
from scipy.ndimage import binary_dilation
from pdf_native_evidence import evidence as native_precision_evidence, verify_frozen_baseline
from pdf_geometry_evidence import inspect_page, verify_outlines, ellipse_evidence, image_crop_evidence
from shared_border_evidence import raster_matches as border_raster_matches

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fixture", default="all")
    parser.add_argument("--reference", choices=["A", "B"], default="A")
    parser.add_argument("--review-file", type=Path)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/pdf-comparison")
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--configuration", choices=["Debug", "Release"], default="Release")
    parser.add_argument("--no-exceptions", action="store_true", help="Disable historical AA exceptions; fixed shared-border contracts remain enforced")
    args = parser.parse_args()
    verify_frozen_baseline()
    args.output.mkdir(parents=True, exist_ok=True)
    if not args.no_generate:
        if not args.no_build:
            subprocess.run(["dotnet", "build", str(ROOT / "tools/ExcelRenderer.PdfComparison"), "-c", args.configuration, "-m:1"], cwd=ROOT, check=True)
        dll = ROOT / f"tools/ExcelRenderer.PdfComparison/bin/{args.configuration}/net10.0/ExcelRenderer.PdfComparison.dll"
        subprocess.run(["dotnet", str(dll), str(args.output.resolve()), args.fixture], cwd=ROOT, check=True)
    reviewed_path = args.review_file or ROOT / "tools/ExcelRenderer.PdfComparison/reviewed-differences.json"
    reviewed = json.loads(reviewed_path.read_text()) if reviewed_path.exists() and not args.no_exceptions else []
    differences = []
    results = []
    for expected_path in sorted(args.output.glob("*/expected.json")):
        expected = json.loads(expected_path.read_text())
        directory = expected_path.parent
        name = expected["name"]
        if args.fixture != "all" and name != args.fixture and not (args.fixture == "commands" and name.startswith("commands-")):
            continue
        reference_path = directory / ("pdfsharp.pdf" if args.reference == "A" else "baseline-skiasharp.pdf")
        left = fitz.open(reference_path)
        right = fitz.open(directory / "skiasharp.pdf")
        native_evidence = native_precision_evidence(directory) if (directory / "baseline-skiasharp.pdf").exists() else None
        record = {"fixture": name, "pages": [], "reference": args.reference, "bytes": {"reference": reference_path.stat().st_size,
                                                           "skiasharp": (directory / "skiasharp.pdf").stat().st_size}}
        def fail(page, region, kind, metrics, reason):
            differences.append({"fixture": name, "page": page, "region": region, "kind": kind,
                                "metrics": metrics, "status": "requires-fix", "reason": reason})
        if len(left) != len(right):
            fail(0, "document", "page-count", [len(left), len(right)], "Selected page count differs")
        for page_number, (lp, rp) in enumerate(zip(left, right), 1):
            page_record = {"page": page_number, "mediaBox": list(rp.mediabox), "regions": []}
            page_record["geometryEvidence"] = inspect_page(lp, rp)
            if native_evidence is not None:
                page_record["nativePrecisionEvidence"] = native_evidence
                if not native_evidence["passed"]:
                    fail(page_number, "native-preservation", "native-preservation", native_evidence, "Native B/C content, encoded streams or object identities changed")
            page_record["sourceFontEvidence"] = {"reference": verify_outlines(lp), "skiasharp": verify_outlines(rp)}
            if name == "images":
                page_record["cropEvidence"] = image_crop_evidence(rp)
                if not page_record["cropEvidence"]["passed"]:
                    fail(page_number, "image-2", "independent-crop", page_record["cropEvidence"], "Image crop differs from independent source pixel oracle")
            if name == "shapes":
                page_record["ellipseEvidence"] = ellipse_evidence(lp, rp)
                if not page_record["ellipseEvidence"]["passed"]:
                    fail(page_number, "Ellipse", "independent-ellipse", page_record["ellipseEvidence"], "Ellipse differs from independent dimensions/equation")
            for box_name in ("mediabox", "cropbox"):
                delta = max(abs(a - b) for a, b in zip(getattr(lp, box_name), getattr(rp, box_name)))
                if delta > .01:
                    fail(page_number, "page", box_name, delta, "Box differs by more than 0.01pt")
            ltext = lp.get_text()
            rtext = rp.get_text()
            poppler_text = subprocess.check_output(["pdftotext", "-f", str(page_number), "-l", str(page_number), "-raw", str(directory / "skiasharp.pdf"), "-"], text=True)
            (directory / f"page-{page_number}-mupdf.txt").write_text(rtext)
            (directory / f"page-{page_number}-poppler.txt").write_text(poppler_text)
            for text in expected.get("expectedText", []):
                for extractor, actual in (("mupdf", rtext), ("poppler", poppler_text)):
                    if text not in actual:
                        fail(page_number, "text", "unicode-" + extractor, {"expected": text, "actual": actual}, "Expected source run is missing or modified")
            def resolved_links(page):
                result = []
                for link in page.get_links():
                    row = {"rect": list(link["from"]), "uri": link.get("uri"), "page": link.get("page"), "to": None,
                           "tooltip": page.parent.xref_get_key(link["xref"], "Contents")[1]}
                    if "nameddest" in link:
                        target, x, y = page.parent.resolve_link("#nameddest=" + link["nameddest"])
                        row.update(page=target, to=[x, y])
                    elif "to" in link:
                        row["to"] = list(link["to"])
                    result.append(row)
                return sorted(result, key=lambda row: (row["rect"], row["uri"] or "", row["page"] or 0))
            left_links, right_links = resolved_links(lp), resolved_links(rp)
            links_equal = len(left_links) == len(right_links)
            for reference, actual in zip(left_links, right_links):
                links_equal &= reference["uri"] == actual["uri"] and reference["page"] == actual["page"] and reference["tooltip"] == actual["tooltip"]
                links_equal &= all(abs(a - b) <= .1 for a, b in zip(reference["rect"], actual["rect"]))
                if reference["to"] is not None and actual["to"] is not None:
                    links_equal &= all(abs(a - b) <= .1 for a, b in zip(reference["to"], actual["to"]))
                else:
                    links_equal &= reference["to"] == actual["to"]
            if not links_equal:
                fail(page_number, "links", "annotations", {"reference": left_links, "skiasharp": right_links}, "Resolved annotations differ beyond 0.1pt or lost URI/destination/tooltip")
            page_record["links"] = right_links
            for dpi in (144, 300):
                reference_pix = lp.get_pixmap(dpi=dpi, alpha=False)
                a = np.frombuffer(reference_pix.samples, np.uint8).reshape(reference_pix.height, reference_pix.width, 3)
                pix = rp.get_pixmap(dpi=dpi, alpha=False)
                b = np.frombuffer(pix.samples, np.uint8).reshape(pix.height, pix.width, 3)
                border_review = next((row.get('sharedBorderContract') for row in (native_evidence or {}).get('contentHashes', []) if row['page'] == page_number), None)
                border_pixels_match = border_raster_matches(border_review, dpi, pix.samples)
                page_record.setdefault('sharedBorderPixels', {})[str(dpi)] = border_pixels_match
                if a.shape != b.shape:
                    fail(page_number, "page", "raster-dimensions", [list(a.shape), list(b.shape)], "No resizing or alignment is permitted")
                    continue
                Image.fromarray(np.maximum(np.abs(a.astype(int) - b.astype(int)), 0).astype(np.uint8)).save(directory / f"{args.reference}-C-page-{page_number}-{dpi}-diff.png")
                Image.fromarray(((a.astype(int) + b.astype(int)) // 2).astype(np.uint8)).save(directory / f"{args.reference}-C-page-{page_number}-{dpi}-overlay.png")
                if expected.get("regions") is not None:
                    regions = [(region["name"], region["bounds"]) for region in expected["regions"]]
                elif expected["path"] == "commands":
                    regions = [(f"text-{i + 1}", (15, 15 + i * 30, min(lp.rect.width, 540), 48 + i * 30)) for i in range(4)]
                    regions += [("line", (15, 165, min(lp.rect.width, 510), 176)), ("fill", (15, 178, 85, 205))]
                    if name == "commands-4":
                        regions += [("fractional-page-edge", (lp.rect.width - 1, 205, lp.rect.width, 235))]
                else:
                    # Each text block is screened separately so empty page area cannot dilute errors.
                    regions = [(f"block-{i + 1}", block[:4]) for i, block in enumerate(lp.get_text("blocks"))]
                    regions += [("page-ink", (0, 0, lp.rect.width, lp.rect.height))]
                for region, bounds in regions:
                    x0, y0, x1, y1 = [max(0, int(round(value * dpi / 72))) for value in bounds]
                    aa, bb = a[y0:y1, x0:x1], b[y0:y1, x0:x1]
                    ia, ib = np.max(255 - aa, axis=2) >= 32, np.max(255 - bb, axis=2) >= 32
                    union = ia | ib
                    area = max(1, int(union.sum()))
                    changed = float(((np.max(np.abs(aa.astype(int) - bb.astype(int)), axis=2) > 16) & union).sum() / area)
                    # Euclidean one-pixel neighborhood, excluding diagonal pixels >1px away.
                    structure = np.array([[0, 1, 0], [1, 1, 1], [0, 1, 0]], bool)
                    unmatched = int((ia & ~binary_dilation(ib, structure=structure)).sum() + (ib & ~binary_dilation(ia, structure=structure)).sum()) / area
                    metrics = {"dpi": dpi, "differentInkRatio": changed, "unmatchedInkRatio": unmatched}
                    page_record["regions"].append({"region": region, **metrics})
                    if (changed > .01 or unmatched > .001) and not border_pixels_match:
                        fail(page_number, region, "raster", metrics, "Exceeds fixed 1% RGB or 0.1% unmatched ink gate")
            record["pages"].append(page_record)
        fonts = []
        for xref in range(1, right.xref_length()):
            if right.xref_get_key(xref, "Length1")[0] != "null":
                data = right.xref_stream(xref)
                if data:
                    try:
                        font = TTFont(io.BytesIO(data))
                        outlines = sum(1 for glyph_name in font.getGlyphOrder() if font["glyf"][glyph_name].numberOfContours != 0) if "glyf" in font else None
                        fonts.append({"bytes": len(data), "glyphs": font["maxp"].numGlyphs, "outlines": outlines})
                    except Exception as error:
                        fail(0, "fonts", "font-parse", str(error), "Embedded font parser rejected a font")
        record["fonts"] = fonts
        # A second parser proves retained glyph IDs and exact outlines against the
        # committed source fonts, including recursively decomposed composite glyphs.
        checked = set()
        source_paths = {"NotoSansJP-Regular": ROOT / "third_party/NotoSansJP/NotoSansJP-Regular.ttf",
                        "IPAmjMincho": ROOT / "third_party/IPAmjMincho/ipamjm.ttf"}
        for page in right:
            for font_info in page.get_fonts(full=True):
                font_xref = font_info[0]
                if font_xref in checked:
                    continue
                checked.add(font_xref)
                extracted = right.extract_font(font_xref)[3]
                if not extracted:
                    continue  # Native Type3 color emoji are not outline fonts.
                subset_font = TTFont(io.BytesIO(extracted))
                ps_name = subset_font["name"].getDebugName(6) if "name" in subset_font else ""
                if ps_name not in source_paths:
                    fail(0, "fonts", "unverified-font", ps_name, "No fixed source font oracle for this embedded outline font")
                    continue
                source_font = TTFont(source_paths[ps_name])
                subset_order, source_order = subset_font.getGlyphOrder(), source_font.getGlyphOrder()
                original_glyphs, subset_glyphs = source_font.getGlyphSet(), subset_font.getGlyphSet()
                used = set()
                for target_page in right:
                    for trace in target_page.get_texttrace():
                        if trace["font"] == ps_name:
                            used.update(character[1] for character in trace["chars"] if character[1] >= 0)
                before_validation = len(differences)
                if not used:
                    fail(0, "fonts", "empty-used-glyph-oracle", ps_name, "The text trace did not resolve a fixed embedded font")
                for glyph_id in used:
                    if glyph_id >= len(subset_order) or glyph_id >= len(source_order):
                        fail(0, "fonts", "missing-used-glyph", glyph_id, "Used glyph ID lies outside the retained font")
                        continue
                    original_pen = DecomposingRecordingPen(original_glyphs)
                    subset_pen = DecomposingRecordingPen(subset_glyphs)
                    original_glyphs[source_order[glyph_id]].draw(original_pen)
                    subset_glyphs[subset_order[glyph_id]].draw(subset_pen)
                    if original_pen.value != subset_pen.value:
                        fail(0, "fonts", "changed-used-outline", glyph_id, "Subset changed an actual used outline or composite closure")
                record.setdefault("glyphValidation", []).append({"font": ps_name, "usedGlyphCount": len(used), "retainedGlyphCount": len(subset_order), "exactSourceOutlines": len(differences) == before_validation})
        if name in ("invoice", "japanese-grid-report") and record["bytes"]["skiasharp"] >= 1024 * 1024:
            fail(0, "fonts", "size", record["bytes"], "Small Japanese report must be below 1 MiB")
        full_path = directory / "skiasharp-full.pdf"
        if full_path.exists():
            record["fullEmbedBytes"] = full_path.stat().st_size
            record["reduction"] = 1 - record["bytes"]["skiasharp"] / full_path.stat().st_size
            if record["reduction"] < .9:
                fail(0, "fonts", "reduction", record["reduction"], "Subset reduction must be at least 90%")
            full = fitz.open(full_path)
            if full[0].get_pixmap(dpi=144).samples != right[0].get_pixmap(dpi=144).samples or full[0].get_text() != right[0].get_text():
                fail(1, "subset", "subset-equivalence", {}, "Subset changed pixels or extracted Unicode")
            full_poppler = subprocess.check_output(["pdftotext", "-raw", str(full_path), "-"], text=True)
            subset_poppler = subprocess.check_output(["pdftotext", "-raw", str(directory / "skiasharp.pdf"), "-"], text=True)
            if full_poppler != subset_poppler:
                fail(1, "subset", "subset-poppler-equivalence", {}, "Subset changed Poppler Unicode extraction")
        raw_path = directory / "skiasharp-raw.pdf"
        if raw_path.exists():
            raw = fitz.open(raw_path)
            if [page.get_text() for page in raw] != [page.get_text() for page in right]:
                fail(1, "finalizer", "unicode-finalization", {}, "Structural processing changed Unicode")
            def resources(document):
                streams = []
                for xref in range(1, document.xref_length()):
                    if document.xref_get_key(xref, "Length1")[0] != "null" or document.xref_get_key(xref, "Subtype")[1] == "/Image":
                        data = document.xref_stream(xref)
                        if data:
                            streams.append(hashlib.sha256(data).hexdigest())
                return sorted(streams)
            if resources(raw) != resources(right):
                fail(1, "finalizer", "changed-resources", {}, "Structural processing changed font/image bytes")
        results.append(record)
    if not results:
        raise SystemExit("No generated fixtures matched")
    for difference in differences:
        if difference["kind"] != "raster":
            continue
        for exception in reviewed:
            if not all(difference[key] == exception[key] for key in ("fixture", "page", "region")) or difference["metrics"]["dpi"] != exception["dpi"]:
                continue
            expected = json.loads((args.output / difference["fixture"] / "expected.json").read_text())
            command_hash = expected.get("commandSha256", expected.get("commandFingerprints", {}).get(str(difference["page"])))
            if command_hash != exception["commandSha256"]:
                difference["reason"] += "; reviewed command fingerprint changed"
                continue
            record = next(record for record in results if record["fixture"] == difference["fixture"])
            page = record["pages"][difference["page"] - 1]
            evidence = page[exception["evidence"]]
            if not evidence["passed"] or not all(font["passed"] for font in page["sourceFontEvidence"].values()):
                difference["reason"] += "; independent structural evidence failed"
                continue
            if any(difference["metrics"][key] > exception[key] for key in ("differentInkRatio", "unmatchedInkRatio")):
                difference["reason"] += "; exceeds individually reviewed raster bound"
                continue
            difference.update(status=exception["status"], reason=exception["reason"], evidence=exception["evidence"])
            break
    requires_fix = sum(difference["status"] == "requires-fix" for difference in differences)
    report = {"versions": {package: importlib.metadata.version(package) for package in ("PyMuPDF", "numpy", "Pillow", "fonttools", "scipy")},
              "gates": {"boxPoints": .01, "rgbDelta": 16, "differentInkRatio": .01, "unmatchedInkRatio": .001},
              "fixtures": results, "screeningDifferences": len(differences), "acceptedDifferences": len(differences) - requires_fix, "requiresFix": requires_fix}
    (args.output / ("report.json" if args.reference == "A" else "report-B-C.json")).write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n")
    (args.output / ("differences.json" if args.reference == "A" else "differences-B-C.json")).write_text(json.dumps(differences, indent=2, ensure_ascii=False) + "\n")
    rows = "".join(f'<tr><td>{html.escape(row["fixture"])}</td><td>{row["page"]}</td><td>{html.escape(row["region"])}</td><td>{html.escape(row["kind"])}</td><td>{html.escape(str(row["metrics"]))}</td><td>{html.escape(row["status"])}</td><td>{html.escape(row["reason"])}</td></tr>' for row in differences)
    image_links = "".join(f'<p><a href="{html.escape(str(path.relative_to(args.output)))}">{html.escape(str(path.relative_to(args.output)))}</a></p>' for path in sorted(args.output.glob("*/*-diff.png")))
    (args.output / ("index.html" if args.reference == "A" else "index-B-C.html")).write_text(f'<!doctype html><meta charset="utf-8"><title>PDF backend comparison</title><h1>PDF backend comparison</h1><p>Requires fix: {requires_fix}; reviewed: {len(differences) - requires_fix}</p><p><a href="report.json">Report</a> · <a href="differences.json">Differences</a></p><table border="1"><tr><th>Fixture</th><th>Page</th><th>Region</th><th>Kind</th><th>Metrics</th><th>Status</th><th>Evidence/reason</th></tr>{rows}</table>{image_links}')
    print(f'{len(results)} fixtures; {requires_fix} unmet gates; {len(differences) - requires_fix} reviewed differences; report: {args.output / "index.html"}')
    return 1 if requires_fix else 0


if __name__ == "__main__":
    sys.exit(main())
