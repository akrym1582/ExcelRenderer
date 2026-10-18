#!/usr/bin/env python3
"""Reject the removed dependency in source, restored graphs, PE AssemblyRefs and distribution outputs."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
FORBIDDEN = re.compile(r"pdf[-_.]?sharp", re.I)
# Every allowed executable occurrence is a reserved-value/rejection or prohibition check.
ALLOWED_LINES = {
    ".github/workflows/unit-tests.yml": {
        'run: python scripts/check-no-pdfsharp.py --packages artifacts/formulas-packages',
        'run: python scripts/check-no-pdfsharp-mutations.py'},
    ".github/workflows/pdf-build-distribution.yml": {
        '- run: python scripts/check-no-pdfsharp.py --packages artifacts/pdf-packages --publish artifacts/pdf-publish'},
    ".github/workflows/verify-font-split.yml": {
        '- run: python scripts/check-no-pdfsharp.py --packages artifacts/packages'},
    "src/ExcelRenderer/PdfBackend.cs": {
        '[Obsolete("PDFsharp has been removed. Specify SkiaSharp.")]', 'PdfSharp = 0,'},
    "src/ExcelRenderer/ExcelConverter.RenderAsync.cs": {
        'throw new NotSupportedException("PDFsharp has been removed. Specify PdfBackend.SkiaSharp.");'},
    "src/ExcelRenderer.Tool/Commands/CommandSupport.cs": {
        'result.AddError("PDFsharp has been removed. Use --pdf-backend skiasharp.");'},
    "tools/ExcelRenderer.PdfComparison/Program.cs": {
        'var original = Path.Combine(directory, "pdfsharp.pdf");',
        'CopyBaseline("links", Path.Combine(directory, "pdfsharp.pdf"));',
        'CopyBaseline(Path.GetFileName(directory), Path.Combine(directory, "pdfsharp.pdf"));',
        'CopyBaseline(sample.Key, Path.Combine(directory, "pdfsharp.pdf"));',
        'var baseline = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/Shared/PdfRemovalBaseline", name, "pdfsharp.pdf"));'},
    "tools/ExcelRenderer.PdfComparison/AssemblyReferenceAudit.cs": {
        'if (references.Any(name => name.Contains("PdfSharp", StringComparison.OrdinalIgnoreCase)))'},
    "tests/ExcelRenderer.Tool.Tests/PdfBackendIntegrationTests.cs": {
        '[InlineData("png", "pdfsharp")]', '[InlineData("markdown", "pdfsharp")]', '[InlineData("pdf", "pdfsharp")]'},
}

ALLOWED_LINES['scripts/validate-formulas-packages.py'] = {
    "assert not any('pdfsharp' in name.lower().replace('-', '').replace('_', '').replace('.', '') for name in libraries), path",
}

ALLOWED_LINES['scripts/compare-core-removal-baselines.py'] = {
    "OUT = ROOT / 'artifacts/pdfsharp-removal-current/core'",
}

# Reviewed historical-reference/prohibition lines; no executable backend implementation.
ALLOWED_LINES.update({'scripts/benchmark-pdf-backends.py': {'if args.include_reference_a: engines.insert(0, ("A", '
                                       'args.baseline_assembly, "pdfsharp"))'},
 'scripts/check-core-dependencies.py': {'assert not '
                                        "re.search(r'\\b(ClosedXML|PDFsharp|SkiaSharp)\\b|ExcelRenderer\\.(Core|Mapping|Fonts)', "
                                        'code), path'},
 'scripts/check-formulas-dependencies.py': {'assert not '
                                            "re.search(r'\\b(ClosedXML|NPOI|ANTLR|DocumentFormat|PDFsharp|SkiaSharp)\\b|ExcelRenderer\\.(Core|Mapping|OpenXml|Fonts)', "
                                            'code), path'},
 'scripts/compare-pdf-backends.py': {'reference_path = directory / ("pdfsharp.pdf" if args.reference == "A" '
                                     'else "baseline-skiasharp.pdf")'},
 'scripts/pdf_geometry_evidence.py': {"# These limits only bound PDFsharp's serialized 3-decimal Tf "
                                      'precision and'},
 'tests/ExcelRenderer.Tests/PdfRemovalContractTests.cs': {'Assert.DoesNotContain(loaded, name => '
                                                          'name.Contains("PdfSharp", '
                                                          'StringComparison.OrdinalIgnoreCase));'}})


def require_clean(text, location):
    if FORBIDDEN.search(text):
        raise RuntimeError(f"Removed PDF dependency found: {location}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--packages', type=Path)
    parser.add_argument('--publish', type=Path)
    parser.add_argument('--configuration', default='Release')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/pdf-removal-dependency-audit.json')
    args = parser.parse_args()
    files = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard'], cwd=ROOT, text=True).splitlines()
    projects = []
    history_path = ROOT / 'scripts/pdf-removal-history.json'
    history = json.loads(history_path.read_text()) if history_path.exists() else {}
    checked = 0
    for name in sorted(set(files)):
        path = ROOT / name
        if not path.is_file():
            continue
        if name in ('scripts/pdf-removal-history.json', 'scripts/check-no-pdfsharp.py', 'scripts/check-no-pdfsharp-mutations.py'):
            continue
        if name in history:
            if hashlib.sha256(path.read_bytes()).hexdigest() != history[name]:
                raise RuntimeError(f'Historical evidence changed: {name}')
            continue
        if path.suffix not in ('.cs', '.csproj', '.props', '.targets', '.json', '.config', '.yml', '.yaml', '.py'):
            continue
        if path.suffix == '.csproj':
            projects.append(path)
        for line in path.read_text(encoding='utf-8-sig').splitlines():
            if name.startswith('src/') and re.search(r'\b(XGraphics|XFont|IFontResolver|GlobalFontSettings|PdfDocument|PdfSharpFontGate)\b', line):
                raise RuntimeError(f'Forbidden legacy implementation type: {name}: {line.strip()}')
            if FORBIDDEN.search(line) and line.strip() not in ALLOWED_LINES.get(name, set()):
                raise RuntimeError(f'Forbidden source/configuration reference: {name}: {line.strip()}')
        checked += 1
    assets = []
    for project in projects:
        restored = project.parent / 'obj/project.assets.json'
        if not restored.exists():
            raise RuntimeError(f'Restore every project before auditing: {project}')
        data = json.loads(restored.read_text())
        for name in data['libraries']:
            require_clean(name, restored)
        assets.append(str(restored.relative_to(ROOT)))
    listed = subprocess.run(['dotnet', 'list', str(ROOT / 'ExcelRenderer.slnx'), 'package', '--include-transitive', '--format', 'json', '--no-restore'], cwd=ROOT, text=True, capture_output=True, check=True)
    graph = json.loads(listed.stdout)
    require_clean(json.dumps(graph), 'dotnet package list including transitives')
    assemblies = set()
    outputs = []
    for project in projects:
        directory = project.parent / 'bin' / args.configuration
        if not directory.is_dir():
            raise RuntimeError(f'Clean build required: {directory}')
        outputs.append(directory)
    if args.publish:
        outputs.append(args.publish.resolve())
    for directory in outputs:
        for path in directory.rglob('*'):
            if not path.is_file():
                continue
            require_clean(path.name, path)
            if path.suffix.lower() == '.dll':
                assemblies.add(path.resolve())
            elif path.name.endswith(('.deps.json', '.runtimeconfig.json')):
                require_clean(path.read_text(), path)
    with tempfile.TemporaryDirectory(prefix='excelrenderer-pdf-removal-audit-') as temporary:
        work = Path(temporary)
        packages = []
        if args.packages:
            for package in sorted(args.packages.glob('*.nupkg')):
                packages.append(package.name)
                with zipfile.ZipFile(package) as archive:
                    for member in archive.namelist():
                        require_clean(member, f'{package}:{member}')
                        if member.endswith(('.nuspec', '.props', '.targets', '.deps.json')):
                            require_clean(archive.read(member).decode('utf-8-sig'), f'{package}:{member}')
                        elif member.lower().endswith('.dll'):
                            target = work / package.stem / member
                            target.parent.mkdir(parents=True, exist_ok=True)
                            target.write_bytes(archive.read(member))
                            assemblies.add(target)
        path_list = work / 'assemblies.json'
        path_list.write_text(json.dumps([str(path) for path in sorted(assemblies)]))
        dll = ROOT / f'tools/ExcelRenderer.PdfComparison/bin/{args.configuration}/net10.0/ExcelRenderer.PdfComparison.dll'
        if not dll.is_file():
            raise RuntimeError('Build the comparison/audit tool before this check')
        audit = subprocess.run(['dotnet', str(dll), '--audit-assemblies', str(path_list)], cwd=ROOT, text=True, capture_output=True)
        if audit.returncode:
            raise RuntimeError(audit.stderr)
        references = json.loads(audit.stdout)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps({'sourceFiles': checked, 'assets': assets, 'packageGraph': graph, 'packages': packages, 'assemblies': references}, indent=2) + '\n')
    print(f'No removed PDF dependency: {checked} source/config files, {len(assets)} restored projects, {len(references)} managed AssemblyRef inventories, {len(packages)} packages')


if __name__ == '__main__':
    main()
