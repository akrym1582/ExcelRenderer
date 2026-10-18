#!/usr/bin/env python3
"""Verify the four-package distribution, including embedded assembly identity."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PUBLISHED = {'ExcelRenderer', 'ExcelRenderer.Mapping', 'ExcelRenderer.Tool', 'ExcelRenderer.Fonts'}
INTERNAL = {'ExcelRenderer.Core', 'ExcelRenderer.OpenXml', 'ExcelRenderer.Formulas'}


def check_projects():
    projects = {}
    for directory in ['src', 'tests', 'samples', 'tools']:
        for path in sorted((ROOT / directory).rglob('*.csproj')):
            if {'bin', 'obj'} & set(path.relative_to(ROOT).parts):
                continue
            result = subprocess.check_output([
                'dotnet', 'msbuild', str(path), '-nologo',
                '-getProperty:IsPackable,PackageId,Version',
            ], text=True)
            properties = json.loads(result)['Properties']
            packable = properties['IsPackable'].lower() == 'true'
            expected = path.relative_to(ROOT).as_posix() in {f'src/{name}/{name}.csproj' for name in PUBLISHED}
            assert packable == expected, (path, properties)
            if packable:
                assert properties['PackageId'] == path.stem, (path, properties)
            projects[str(path.relative_to(ROOT))] = properties
    assert {Path(path).stem for path in projects if projects[path]['IsPackable'].lower() == 'true'} == PUBLISHED
    assert len({properties['Version'] for path, properties in projects.items() if Path(path).stem in PUBLISHED}) == 1, 'Published package versions must be aligned'
    return projects


def check_packages(packages):
    versions = {name: ET.parse(ROOT / 'src' / name / f'{name}.csproj').findtext('./PropertyGroup/Version') for name in PUBLISHED}
    expected = {f'{name}.{versions[name]}.nupkg' for name in PUBLISHED}
    actual = {path.name for path in packages.glob('*.nupkg')}
    assert actual == expected, (actual, expected)
    inventory, hashes = {}, {}
    for name in sorted(PUBLISHED):
        with zipfile.ZipFile(packages / f'{name}.{versions[name]}.nupkg') as archive:
            names = archive.namelist()
            assert not any('harfbuzzsubset' in path.lower() or 'harfbuzz-copying' in path.lower() for path in names), (name, 'Removed native subset artifact in package')
            spec = ET.fromstring(archive.read(next(path for path in names if path.endswith('.nuspec'))))
            dependencies = {node.attrib['id']: node.attrib['version'] for node in spec.iter() if node.tag.rsplit('}', 1)[-1] == 'dependency'}
            assert not INTERNAL.intersection(dependencies), (name, dependencies)
            assert not any(dep.startswith(('ClosedXML', 'NPOI', 'Antlr')) for dep in dependencies), (name, dependencies)
            dlls = [path for path in names if path.endswith('.dll')]
            if name == 'ExcelRenderer.Fonts':
                assert sum(path.endswith('/ExcelRenderer.Fonts.dll') for path in dlls) == 1
            embedded = {}
            assemblies = {'ExcelRenderer.OpenXml', 'ExcelRenderer.Formulas'} if name in {'ExcelRenderer', 'ExcelRenderer.Mapping'} else set()
            if name == 'ExcelRenderer':
                assemblies.add('ExcelRenderer.Core')
            if name == 'ExcelRenderer.Tool':
                assemblies = INTERNAL | {'ExcelRenderer.Mapping', 'ExcelRenderer', 'ExcelRenderer.Fonts'}
            for assembly in sorted(assemblies):
                for extension in (['dll'] if name == 'ExcelRenderer.Tool' else ['dll', 'xml']):
                    matches = [path for path in names if path.endswith(f'/{assembly}.{extension}')]
                    assert len(matches) == 1, (name, assembly, extension, matches)
                    if name != 'ExcelRenderer.Tool':
                        assert matches[0].startswith('lib/netstandard2.1/'), matches
                    digest = hashlib.sha256(archive.read(matches[0])).hexdigest()
                    key = f'{assembly}.{extension}'
                    assert hashes.setdefault(key, digest) == digest, (name, key, 'Bundled assemblies differ')
                    embedded[key] = digest
            if name in {'ExcelRenderer', 'ExcelRenderer.Mapping'}:
                assert dependencies['ExcelNumberFormat'] == '1.1.0', (name, dependencies)
                assert dependencies['DocumentFormat.OpenXml'] == '3.3.0', (name, dependencies)
            inventory[name] = {'version': versions[name], 'dependencies': dependencies, 'dlls': dlls, 'embedded_sha256': embedded}
    return inventory


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--packages', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    report = {'projects': check_projects()}
    if args.packages:
        report['packages'] = check_packages(args.packages.resolve())
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2) + '\n')
    print(f"Verified {len(report['projects'])} projects: only {', '.join(sorted(PUBLISHED))} are packable")
    if args.packages:
        print('Exactly four packages; no unpublished dependencies; shared DLL/XML hashes match')
