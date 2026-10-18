#!/usr/bin/env python3
"""Run warmed fresh-process PDF backend measurements; preserve raw samples and gate ratios."""
import argparse
import copy
import concurrent.futures
import json
import io
import posixpath
import random
from pathlib import Path
import statistics
import subprocess
import sys
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/pdf-benchmark")
    parser.add_argument("--repeats", type=int, default=5)
    parser.add_argument("--fixture", choices=["all", "invoice", "grid", "images", "50-pages", "200-pages"], default="all")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--configuration", choices=["Debug", "Release"], default="Release")
    parser.add_argument("--warmups", type=int, default=3)
    parser.add_argument("--generate-only", action="store_true")
    args = parser.parse_args()
    if args.repeats < 5 or args.warmups < 1:
        parser.error("Use at least five fresh-process rounds and one warm-up per process")
    dll = ROOT / f"tools/ExcelRenderer.Performance/bin/{args.configuration}/net10.0/ExcelRenderer.Performance.dll"
    args.output.mkdir(parents=True, exist_ok=True)
    if not args.no_build:
        subprocess.run(["dotnet", "build", str(ROOT / "tools/ExcelRenderer.Performance"), "-c", args.configuration, "-m:1", "--verbosity:minimal"], cwd=ROOT, check=True)
    (args.output / "conditions.json").write_text(json.dumps({"configuration": args.configuration, "warmups": args.warmups, "rounds": args.repeats,
        "processConcurrency": [1, 4], "fixture": args.fixture}, indent=2))
    fixtures = {"invoice": ROOT / "samples/mapping/invoice/invoice.xlsx", "grid": ROOT / "samples/excel/japanese-grid-report.xlsx"}
    # Manual row breaks and a four-column print area make the page workloads exact.
    for name, rows, mode in [("images", 120, "images"), ("50-pages", 900, "text"), ("200-pages", 3600, "text")]:
        if args.fixture in ("all", name):
            path = args.output / f"{name}.xlsx"
            subprocess.run(["dotnet", str(dll), "generate", str(path), str(rows), mode], cwd=ROOT, check=True)
            if name in ("50-pages", "200-pages"):
                namespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                tag = lambda local: "{" + namespace + "}" + local
                with zipfile.ZipFile(path) as archive:
                    entries = {entry.filename: archive.read(entry.filename) for entry in archive.infolist()}
                workbook = ET.fromstring(entries["xl/workbook.xml"])
                for area in workbook.iter(tag("definedName")):
                    if area.get("name") == "_xlnm.Print_Area":
                        area.text = f"'Synthetic'!$A$1:$D${rows}"
                sheet_key = next(key for key in entries if key.startswith("xl/worksheets/sheet") and key.endswith(".xml"))
                sheet = ET.fromstring(entries[sheet_key])
                breaks = ET.SubElement(sheet, tag("rowBreaks"), count=str(rows // 18 - 1), manualBreakCount=str(rows // 18 - 1))
                for row in range(18, rows, 18):
                    ET.SubElement(breaks, tag("brk"), id=str(row), min="0", max="16383", man="1")
                entries["xl/workbook.xml"] = ET.tostring(workbook, encoding="utf-8", xml_declaration=True)
                entries[sheet_key] = ET.tostring(sheet, encoding="utf-8", xml_declaration=True)
                with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
                    for key, data in entries.items():
                        archive.writestr(key, data)
            if name == "images":
                from PIL import Image
                with zipfile.ZipFile(path) as archive:
                    entries = {entry.filename: archive.read(entry.filename) for entry in archive.infolist()}
                drawing_key = next(key for key in entries if key.startswith("xl/drawings/drawing") and key.endswith(".xml"))
                drawing = ET.fromstring(entries[drawing_key])
                relationships_key = str(Path(drawing_key).parent / '_rels' / (Path(drawing_key).name + '.rels'))
                relationships = ET.fromstring(entries[relationships_key])
                image_relation = next(relation for relation in relationships if relation.get('Type', '').endswith('/image'))
                original_key = posixpath.normpath(posixpath.join(posixpath.dirname(drawing_key), image_relation.get('Target'))).lstrip('/')
                relationships.remove(image_relation)
                del entries[original_key]
                anchor = copy.deepcopy(list(drawing)[0])
                drawing.clear()
                for index in range(32):
                    image_key = f'xl/media/pdf-benchmark-image-{index}.png'
                    encoded = io.BytesIO()
                    noise = random.Random(20261018 + index).randbytes(256 * 256 * 3)
                    picture_pixels = Image.frombytes('RGB', (256, 256), noise)
                    if index % 2:
                        picture_pixels.putalpha(192)
                    picture_pixels.save(encoded, format='PNG')
                    entries[image_key] = encoded.getvalue()
                    image_id = f'pdfBenchmarkImage{index}'
                    ET.SubElement(relationships, '{http://schemas.openxmlformats.org/package/2006/relationships}Relationship',
                                  Id=image_id, Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/image',
                                  Target='../media/' + posixpath.basename(image_key))
                    picture = copy.deepcopy(anchor)
                    for element in picture.iter():
                        local = element.tag.rsplit("}", 1)[-1]
                        if local == "row": element.text = str(index * 3)
                        if local == "col": element.text = str(index % 4)
                        if local == "ext" and "cx" in element.attrib:
                            element.set("cx", str(100 * 9525)); element.set("cy", str(100 * 9525))
                        if local == "cNvPr": element.set("id", str(index + 1))
                        if local == "blip": element.set('{http://schemas.openxmlformats.org/officeDocument/2006/relationships}embed', image_id)
                    drawing.append(picture)
                entries[drawing_key] = ET.tostring(drawing, encoding="utf-8", xml_declaration=True)
                entries[relationships_key] = ET.tostring(relationships, encoding="utf-8", xml_declaration=True)
                with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
                    for key, data in entries.items(): archive.writestr(key, data)
            fixtures[name] = path
    if args.generate_only:
        return 0
    raw = []
    def sample(name, path, backend, concurrency, iteration):
        command = ["dotnet", str(dll), "render-case", str(path), "pdf", "paginated", str(args.warmups + 1), "--font-pack", "--pdf-backend", backend]
        def execute(_):
            process = subprocess.run(command, cwd=ROOT, check=True, text=True, capture_output=True)
            records = [json.loads(line) for line in process.stdout.splitlines() if line.startswith("{")]
            measured = [record for record in records if "TotalMs" in record][-1]
            return {"fixture": name, "backend": backend, "concurrency": concurrency, "sample": iteration, **measured}
        # Each worker is a fresh process; its first conversion warms native resources.
        with concurrent.futures.ThreadPoolExecutor(max_workers=concurrency) as pool:
            return list(pool.map(execute, range(concurrency)))
    summary = []
    for name, path in fixtures.items():
        if args.fixture not in ("all", name):
            continue
        for concurrency in (1, 4):
            groups = {}
            for backend in ("pdfsharp", "skiasharp"):
                values = []
                for iteration in range(args.repeats):
                    values += sample(name, path, backend, concurrency, iteration)
                raw += values
                (args.output / "raw.json").write_text(json.dumps(raw, indent=2))
                groups[backend] = {metric: {"median": statistics.median(value[metric] for value in values), "max": max(value[metric] for value in values)}
                                   for metric in ("TotalMs", "PeakWorkingSet64", "AllocatedBytes", "OutputBytes")}
                groups[backend]["pages"] = sorted(set(value["Pages"] for value in values))
                if name in ("50-pages", "200-pages") and groups[backend]["pages"] != [int(name.split("-")[0])]:
                    raise RuntimeError(f"Incorrect page workload: {name}, {groups[backend]['pages']}")
                for phase in ("pdfSubset.ms", "pdfPage.ms", "pdfFinalSave.ms"):
                    groups[backend][phase] = statistics.median(value["Metrics"].get(phase, 0) for value in values)
            time_ratio = groups["skiasharp"]["TotalMs"]["median"] / groups["pdfsharp"]["TotalMs"]["median"]
            memory_ratio = groups["skiasharp"]["PeakWorkingSet64"]["median"] / groups["pdfsharp"]["PeakWorkingSet64"]["median"]
            summary.append({"fixture": name, "concurrency": concurrency, "engines": groups,
                            "timeRatio": time_ratio, "memoryRatio": memory_ratio, "passed": time_ratio <= 2 and memory_ratio <= 2})
            print(json.dumps(summary[-1]), flush=True)
    (args.output / "summary.json").write_text(json.dumps(summary, indent=2))
    return 0 if summary and all(row["passed"] for row in summary) else 1


if __name__ == "__main__":
    sys.exit(main())
