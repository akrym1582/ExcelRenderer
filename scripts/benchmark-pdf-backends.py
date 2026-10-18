#!/usr/bin/env python3
"""Measure current Skia output; optionally compare a separately built, frozen stage-B assembly."""
import argparse
import copy
import hashlib
import platform
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
    parser.add_argument("--fixture", choices=["all", "invoice", "grid", "images", "50-pages", "200-pages", "rich-text", "borders"], default="all")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--baseline-assembly", type=Path, help="Manual comparison only: frozen first-stage benchmark DLL outside the final solution")
    parser.add_argument("--parallelism", nargs="+", type=int, default=[1, 4, 8])
    parser.add_argument("--include-reference-a", action="store_true", help="Also measure the historical A renderer in the explicitly supplied frozen assembly")
    parser.add_argument("--configuration", choices=["Debug", "Release"], default="Release")
    parser.add_argument("--warmups", type=int, default=3)
    parser.add_argument("--generate-only", action="store_true")
    parser.add_argument("--inputs", type=Path, help="Reuse exact generated workbook bytes from an earlier run")
    args = parser.parse_args()
    if args.repeats < 5 or args.warmups < 1:
        parser.error("Use at least five fresh-process rounds and one warm-up per process")
    dll = ROOT / f"tools/ExcelRenderer.Performance/bin/{args.configuration}/net10.0/ExcelRenderer.Performance.dll"
    args.output.mkdir(parents=True, exist_ok=True)
    if not args.no_build:
        subprocess.run(["dotnet", "build", str(ROOT / "tools/ExcelRenderer.Performance"), "-c", args.configuration, "-m:1", "--verbosity:minimal"], cwd=ROOT, check=True)
    (args.output / "conditions.json").write_text(json.dumps({"configuration": args.configuration, "warmups": args.warmups, "rounds": args.repeats,
        "processConcurrency": args.parallelism, "fixture": args.fixture, "baselineAssembly": str(args.baseline_assembly) if args.baseline_assembly else None, "sdk": subprocess.check_output(["dotnet", "--version"], text=True).strip(), "os": platform.platform(), "comparisonTarget": "B: old measurement + Skia subset", "timeAndPeakRssTarget": 1.25,
        "stageOrder": "alternating forward/reverse per fresh-process round", "reusedInputs": str(args.inputs) if args.inputs else None}, indent=2))
    fixtures = {"invoice": ROOT / "samples/mapping/invoice/invoice.xlsx", "grid": ROOT / "samples/excel/japanese-grid-report.xlsx"}
    # Manual row breaks and a four-column print area make the page workloads exact.
    for name, rows, mode in [("images", 120, "images"), ("50-pages", 900, "text"), ("200-pages", 3600, "text")]:
        if args.fixture in ("all", name):
            if args.inputs:
                fixtures[name] = args.inputs.resolve() / f"{name}.xlsx"
                if not fixtures[name].is_file():
                    parser.error(f"Missing frozen input: {fixtures[name]}")
                continue
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
    for name in ("rich-text", "borders"):
        if args.fixture not in ("all", name):
            continue
        if args.inputs:
            fixtures[name] = args.inputs.resolve() / f"{name}.xlsx"
            if not fixtures[name].is_file():
                parser.error(f"Missing frozen input: {fixtures[name]}")
            continue
        path = args.output / (name + ".xlsx")
        subprocess.run(["dotnet", str(dll), "generate", str(path), "40" if name == "rich-text" else "300", "text"], cwd=ROOT, check=True)
        with zipfile.ZipFile(path) as archive:
            entries = {item.filename: archive.read(item.filename) for item in archive.infolist()}
        ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
        q = lambda name: "{" + ns + "}" + name
        styles_key = "xl/styles.xml"
        sheet_key = next(key for key in entries if key.startswith("xl/worksheets/sheet") and key.endswith(".xml"))
        styles, sheet = ET.fromstring(entries[styles_key]), ET.fromstring(entries[sheet_key])
        formats = styles.find(q("cellXfs"))
        if name == "rich-text":
            for row in sheet.iter(q("row")):
                for cell in list(row):
                    if cell.tag == q("c") and cell.get("r").rstrip("0123456789") not in ("A", "B"):
                        row.remove(cell)
            workbook = ET.fromstring(entries['xl/workbook.xml'])
            for area in workbook.iter(q('definedName')):
                if area.get('name') == '_xlnm.Print_Area':
                    area.text = "'Synthetic'!$A$1:$B$40"
            entries['xl/workbook.xml'] = ET.tostring(workbook, encoding="utf-8", xml_declaration=True)
            for cell_format in formats:
                alignment = cell_format.find(q('alignment'))
                if alignment is None:
                    alignment = ET.SubElement(cell_format, q('alignment'))
                alignment.set('wrapText', '1')
            for index, cell in enumerate(sheet.iter(q("c"))):
                for child in list(cell): cell.remove(child)
                cell.set("t", "inlineStr")
                text = ET.SubElement(cell, q("is"))
                for size, value in [("9", "Long rich text 日本語 " * 12), ("12", f" styled ABC row {index} " * 4)]:
                    run = ET.SubElement(text, q("r"))
                    props = ET.SubElement(run, q("rPr"))
                    ET.SubElement(props, q("sz"), {"val": size})
                    ET.SubElement(props, q("rFont"), {"val": "Noto Sans JP"})
                    ET.SubElement(run, q("t"), {"{http://www.w3.org/XML/1998/namespace}space": "preserve"}).text = value
        else:
            borders = styles.find(q("borders"))
            for line in ("thin", "medium", "dashed", "dotted", "double", "dashDot", "dashDotDot", "slantDashDot"):
                border = ET.SubElement(borders, q("border"))
                for side in ("left", "right", "top", "bottom"):
                    ET.SubElement(ET.SubElement(border, q(side), {"style": line}), q("color"), {"rgb": "FF203040"})
                style = copy.deepcopy(formats[0]); style.set("borderId", str(len(borders) - 1)); formats.append(style)
            for index, cell in enumerate(sheet.iter(q("c"))): cell.set("s", str(1 + index % 8))
        for element in (formats, styles.find(q("borders"))): element.set("count", str(len(element)))
        entries[styles_key] = ET.tostring(styles, encoding="utf-8", xml_declaration=True)
        entries[sheet_key] = ET.tostring(sheet, encoding="utf-8", xml_declaration=True)
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
            for key, data in entries.items(): archive.writestr(key, data)
        fixtures[name] = path
    if args.baseline_assembly and not args.baseline_assembly.is_file():
        parser.error("The manual frozen baseline assembly does not exist")
    if args.include_reference_a and not args.baseline_assembly:
        parser.error("Reference A requires an explicitly supplied frozen baseline assembly")
    if not all(value in (1, 4, 8) for value in args.parallelism):
        parser.error("Parallelism must be selected from 1, 4, 8")
    (args.output / "inputs.json").write_text(json.dumps({name: {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()} for name, path in fixtures.items()}, indent=2))
    if args.generate_only:
        return 0
    raw = []
    def sample(name, path, label, assembly, backend, concurrency, iteration):
        command = ["dotnet", str(assembly.resolve()), "render-case", str(path.resolve()), "pdf", "paginated", str(args.warmups + 1), "--font-pack", "--measure-text", "--pdf-backend", backend]
        def execute(worker):
            process = subprocess.run(command, cwd=ROOT, check=True, text=True, capture_output=True)
            records = [json.loads(line) for line in process.stdout.splitlines() if line.startswith("{")]
            measured = [record for record in records if "TotalMs" in record][-1]
            return {"fixture": name, "stage": label, "concurrency": concurrency, "sample": iteration, "worker": worker, **measured}
        with concurrent.futures.ThreadPoolExecutor(max_workers=concurrency) as pool:
            return list(pool.map(execute, range(concurrency)))
    summary = []
    engines = [("C", dll, "skiasharp")]
    if args.baseline_assembly:
        engines.insert(0, ("B", args.baseline_assembly, "skiasharp"))
        if args.include_reference_a: engines.insert(0, ("A", args.baseline_assembly, "pdfsharp"))
    (args.output / "assemblies.json").write_text(json.dumps({label: {"path": str(assembly.resolve()), "dllSha256": hashlib.sha256(assembly.read_bytes()).hexdigest(), "mainSha256": hashlib.sha256((assembly.parent / "ExcelRenderer.dll").read_bytes()).hexdigest(), "coreSha256": hashlib.sha256((assembly.parent / "ExcelRenderer.Core.dll").read_bytes()).hexdigest()} for label, assembly, _ in engines}, indent=2))
    for name, path in fixtures.items():
        if args.fixture not in ("all", name):
            continue
        for concurrency in args.parallelism:
            groups = {}
            stage_values = {label: [] for label, _, _ in engines}
            for iteration in range(args.repeats):
                ordered = engines if iteration % 2 == 0 else list(reversed(engines))
                for label, assembly, backend in ordered:
                    values = sample(name, path, label, assembly, backend, concurrency, iteration)
                    stage_values[label] += values
                    raw += values
                    (args.output / "raw.json").write_text(json.dumps(raw, indent=2))
            for label, assembly, backend in engines:
                values = stage_values[label]
                groups[label] = {metric: {"median": statistics.median(value[metric] for value in values), "max": max(value[metric] for value in values)}
                                 for metric in ("TotalMs", "PeakWorkingSet64", "AllocatedBytes", "OutputBytes")}
                groups[label]["pages"] = sorted(set(value["Pages"] for value in values))
                if name in ("50-pages", "200-pages") and groups[label]["pages"] != [int(name.split("-")[0])]:
                    raise RuntimeError(f"Incorrect page workload: {name}, {groups[label]['pages']}")
                for phase in ("textMeasure.ms", "pdfSubset.ms", "pdfPage.ms", "pdfFinalSave.ms"):
                    phase_values = [value["Metrics"].get(phase, 0) for value in values]
                    groups[label][phase] = {"median": statistics.median(phase_values), "max": max(phase_values)}
            time_ratio = groups["C"]["TotalMs"]["median"] / groups["B"]["TotalMs"]["median"] if "B" in groups else None
            memory_ratio = groups["C"]["PeakWorkingSet64"]["median"] / groups["B"]["PeakWorkingSet64"]["median"] if "B" in groups else None
            if any(group['pages'] != groups['C']['pages'] for group in groups.values()):
                raise RuntimeError(f'Benchmark page plan differs: {name}, {groups}')
            summary.append({"fixture": name, "concurrency": concurrency, "stages": groups,
                            "timeRatio": time_ratio, "memoryRatio": memory_ratio,
                            "timeMaxRatio": groups["C"]["TotalMs"]["max"] / groups["B"]["TotalMs"]["max"] if "B" in groups else None,
                            "memoryMaxRatio": groups["C"]["PeakWorkingSet64"]["max"] / groups["B"]["PeakWorkingSet64"]["max"] if "B" in groups else None,
                            "passed": time_ratio is None or (time_ratio <= 1.25 and memory_ratio <= 1.25)})
            (args.output / "summary.json").write_text(json.dumps(summary, indent=2))
            print(json.dumps(summary[-1]), flush=True)
    return 0 if summary and all(row["passed"] for row in summary) else 1


if __name__ == "__main__":
    sys.exit(main())
