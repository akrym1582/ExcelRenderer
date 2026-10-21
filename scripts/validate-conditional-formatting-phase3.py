#!/usr/bin/env python3
"""Independently parse/rasterize retained Phase 3 PDFs and PostScript; compare public APIs."""
import argparse
import hashlib
import json
import runpy
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def run(*command):
    result = subprocess.run([str(arg) for arg in command], capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    return result.stdout


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--artifacts', type=Path, required=True)
    parser.add_argument('--baseline', type=Path)
    args = parser.parse_args()
    work = args.artifacts.resolve()
    evidence = work / 'independent'
    evidence.mkdir(parents=True, exist_ok=True)
    results = {}
    for path in sorted(work.iterdir()):
        if path.suffix not in ('.pdf', '.ps', '.postscript'):
            continue
        raster = evidence / (path.name + '-%d.png')
        log = run('gs', '-q', '-dSAFER', '-dBATCH', '-dNOPAUSE', '-sDEVICE=png16m', '-r96', f'-sOutputFile={raster}', path)
        results[path.name] = dict(sha256=hashlib.sha256(path.read_bytes()).hexdigest(), ghostscript='passed', raster_pages=len(list(evidence.glob(path.name + '-*.png'))), log=log)
        assert results[path.name]['raster_pages'] > 0
        if path.suffix == '.pdf':
            results[path.name]['pdfinfo'] = run('pdfinfo', path)
    (work / 'independent-validation.json').write_text(json.dumps(results, indent=2) + '\n')
    if args.baseline:
        api = evidence / 'api'
        api.mkdir(exist_ok=True)
        (api / 'Directory.Build.props').write_text('<Project/>\n')
        (api / 'Directory.Build.targets').write_text('<Project/>\n')
        (api / 'snapshot.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>')
        (api / 'Program.cs').write_text(runpy.run_path(str(ROOT / 'scripts/compare-core-api.py'))['SNAPSHOT_SOURCE'])
        run('dotnet', 'build', api / 'snapshot.csproj', '-c', 'Release', '-m:1', '-p:NuGetAudit=false')
        executable = api / 'bin/Release/net10.0/snapshot.dll'
        compatibility = {}
        for product in ('ExcelRenderer', 'ExcelRenderer.Core', 'ExcelRenderer.OpenXml.Reader'):
            snapshots = []
            for tag, tree in [('before', args.baseline.resolve()), ('after', ROOT)]:
                runtime = tree / 'src/ExcelRenderer.Tool/bin/Release/net10.0'
                text = run('dotnet', executable, runtime / (product + '.dll'), runtime)
                (api / f'{product}-{tag}.txt').write_text(text)
                snapshots.append(set(text.splitlines()))
            removed = sorted(snapshots[0] - snapshots[1])
            added = sorted(snapshots[1] - snapshots[0])
            assert not removed, f'{product}: removed API contracts: {removed}'
            if product != 'ExcelRenderer.OpenXml.Reader':
                assert not added, f'{product}: unexpected public API: {added}'
            compatibility[product] = dict(removed=removed, added=added)
        (work / 'api-compatibility.json').write_text(json.dumps(compatibility, indent=2) + '\n')
    print(f'Validated {len(results)} PDF/PostScript files')


if __name__ == '__main__':
    main()
