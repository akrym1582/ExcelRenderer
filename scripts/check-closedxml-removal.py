#!/usr/bin/env python3
"""Verify active source, restored graphs and built artifacts after dependency removal."""
import argparse
import hashlib
import json
import pathlib
import re
import zipfile
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
REMOVED = re.compile(r"closedxml(?:\.parser)?|^rbush$|^sixlabors\.fonts$|^xlparser$", re.I)
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--packages", type=pathlib.Path)
parser.add_argument("--output", type=pathlib.Path, required=True)
args = parser.parse_args()
projects = []
artifacts = []
for directory in ("src", "tests", "tools", "samples"):
    for project in sorted((ROOT / directory).rglob("*.csproj")):
        if {"bin", "obj"} & set(project.parts):
            continue
        for reference in ET.parse(project).findall(".//PackageReference"):
            assert not REMOVED.search(reference.attrib.get("Include", "")), project
        graph = project.parent / "obj/project.assets.json"
        assert graph.exists(), f"Restore required: {project}"
        data = json.loads(graph.read_text())
        bad = [name for name in data["libraries"] if REMOVED.search(name.split("/")[0])]
        assert not bad, (project, bad)
        lock = project.parent / "packages.lock.json"
        if lock.exists():
            entries = json.loads(lock.read_text())["dependencies"]
            assert not any(REMOVED.search(name) for target in entries.values() for name in target), lock
        projects.append({"project": str(project.relative_to(ROOT)), "removedDependencies": bad})
    for source in (ROOT / directory).rglob("*.cs"):
        if {"bin", "obj"} & set(source.parts):
            continue
        assert not re.search(r"using ClosedXML|\b(?:XLWorkbook|IXLCell|IXLWorksheet|IXLGraphicEngine)\b", source.read_text()), source
    for artifact in (ROOT / directory).rglob("*"):
        if not artifact.is_file() or not ({"bin", "obj"} & set(artifact.parts)):
            continue
        if artifact.suffix.lower() == ".dll":
            assert not REMOVED.search(artifact.stem), artifact
        elif artifact.name.endswith(".deps.json"):
            data = json.loads(artifact.read_text())
            assert not any(REMOVED.search(name.split("/")[0]) for name in data.get("libraries", {})), artifact
            artifacts.append(str(artifact.relative_to(ROOT)))
packages = []
core_hashes = []
if args.packages:
    for path in sorted(args.packages.glob("*.nupkg")):
        assert not path.name.startswith("ExcelRenderer.Core."), path
        with zipfile.ZipFile(path) as package:
            files = package.namelist()
            assert not any(REMOVED.search(pathlib.PurePosixPath(name).stem) for name in files if name.endswith(".dll")), path
            nuspec = ET.fromstring(package.read(next(name for name in files if name.endswith(".nuspec"))))
            dependencies = [item.attrib["id"] for item in nuspec.iter() if item.tag.rsplit("}", 1)[-1] == "dependency"]
            assert not any(REMOVED.search(name) for name in dependencies), path
            for name in files:
                if name.endswith(".deps.json"):
                    data = json.loads(package.read(name))
                    assert not any(REMOVED.search(key.split("/")[0]) for key in data.get("libraries", {})), (path, name)
            core = [name for name in files if name.endswith("/ExcelRenderer.Core.dll")]
            xml = [name for name in files if name.endswith("/ExcelRenderer.Core.xml")]
            if path.name.startswith(("ExcelRenderer.1.", "ExcelRenderer.Tool.")):
                assert len(core) == len(xml) == 1, (path, core, xml)
                core_hashes.append(hashlib.sha256(package.read(core[0])).hexdigest())
            packages.append({"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "dependencies": dependencies, "coreDll": core, "coreXml": xml})
    assert len(core_hashes) == 2 and len(set(core_hashes)) == 1, core_hashes
report = {"fullRemoval": True, "projects": projects, "depsJsonChecked": artifacts, "packages": packages, "mainAndToolCoreDllSha256": core_hashes[0] if core_hashes else None}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(report, indent=2) + "\n")
print(f"Passed: {len(projects)} restored projects, {len(artifacts)} dependency manifests, {len(packages)} packages")
