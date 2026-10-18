#!/usr/bin/env python3
"""Prove that the removal audit detects source, graph, metadata and package contamination."""
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
AUDIT = [sys.executable, str(ROOT / 'scripts/check-no-pdfsharp.py')]


def check(label, expected=None, extra=()):
    result = subprocess.run(AUDIT + list(extra), cwd=ROOT, text=True, capture_output=True)
    output = result.stdout + result.stderr
    if expected is None:
        assert result.returncode == 0, output
    else:
        assert result.returncode != 0 and expected in output, (label, output)
    print(label + ': ' + ('passed' if expected is None else 'contamination detected'), flush=True)


def main():
    check('baseline')
    source = ROOT / 'src/ExcelRenderer/RemovedDependencyMutation.csproj'
    assets = ROOT / 'src/ExcelRenderer/obj/project.assets.json'
    original = assets.read_bytes()
    hidden = ROOT / 'src/ExcelRenderer/bin/Release/netstandard2.1/HarmlessAuditFixture.dll'
    assert not source.exists() and not hidden.exists()
    try:
        source.write_text('<Project><ItemGroup><PackageReference Include="PDFsharp" Version="6.2.3" PrivateAssets="all" /></ItemGroup></Project>')
        check('PrivateAssets source reference', 'Forbidden source/configuration reference')
        source.unlink()
        graph = json.loads(original)
        graph['libraries']['PDFsharp/6.2.3'] = {'type': 'package', 'path': 'pdfsharp/6.2.3'}
        assets.write_text(json.dumps(graph))
        check('restored transitive graph', 'Removed PDF dependency found')
        assets.write_bytes(original)
        with tempfile.TemporaryDirectory(prefix='excelrenderer-dependency-mutations-') as temporary:
            work = Path(temporary)
            reference = work / 'reference'
            consumer = work / 'consumer'
            reference.mkdir()
            consumer.mkdir()
            (reference / 'reference.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>PdfSharp.MutationFixture</AssemblyName></PropertyGroup></Project>')
            (reference / 'Marker.cs').write_text('namespace Mutation; public class Marker {}')
            (consumer / 'consumer.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>HarmlessAuditFixture</AssemblyName></PropertyGroup><ItemGroup><ProjectReference Include="../reference/reference.csproj" /></ItemGroup></Project>')
            (consumer / 'UsesMarker.cs').write_text('public class UsesMarker { public Mutation.Marker Value; }')
            subprocess.run(['dotnet', 'build', str(consumer), '-c', 'Release', '-m:1', '-p:UseSharedCompilation=false', '--verbosity:quiet'], check=True, capture_output=True, text=True)
            shutil.copyfile(consumer / 'bin/Release/net10.0/HarmlessAuditFixture.dll', hidden)
            check('AssemblyRef behind an innocent filename', 'Forbidden assembly reference')
            hidden.unlink()
            packages = work / 'packages'
            packages.mkdir()
            with zipfile.ZipFile(packages / 'Harmless.1.0.0.nupkg', 'w') as archive:
                archive.writestr('Harmless.nuspec', '<package><metadata><id>Harmless</id><version>1.0.0</version><dependencies><dependency id="PDFsharp" version="6.2.3" /></dependencies></metadata></package>')
            check('packed transitive dependency', 'Removed PDF dependency found', ['--packages', str(packages)])
    finally:
        source.unlink(missing_ok=True)
        hidden.unlink(missing_ok=True)
        assets.write_bytes(original)
    check('restored audit')


if __name__ == '__main__':
    main()
