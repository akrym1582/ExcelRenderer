#!/usr/bin/env python3
"""Verify optional fonts and CLI bundling with isolated package-only consumers."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

from package_distribution import ROOT, check_packages


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--packages', type=Path, required=True)
    parser.add_argument('--work', type=Path, required=True)
    args = parser.parse_args()
    packages, parent = args.packages.resolve(), args.work.resolve()
    if parent.is_relative_to(ROOT):
        parser.error('--work must be outside the repository to isolate consumers from its build settings')
    inventory = check_packages(packages)
    version = inventory['ExcelRenderer']['version']
    paths = {name: packages / f'{name}.{record["version"]}.nupkg' for name, record in inventory.items()}
    assert paths['ExcelRenderer'].stat().st_size < 3_000_000
    assert paths['ExcelRenderer.Fonts'].stat().st_size > 20_000_000
    with zipfile.ZipFile(paths['ExcelRenderer']) as main_package, \
            zipfile.ZipFile(paths['ExcelRenderer.Fonts']) as fonts, \
            zipfile.ZipFile(paths['ExcelRenderer.Tool']) as tool:
        assert not any(name.endswith('/ExcelRenderer.Fonts.dll') for name in main_package.namelist())
        font_dll = next(name for name in fonts.namelist() if name.endswith('/ExcelRenderer.Fonts.dll'))
        tool_fonts = [name for name in tool.namelist() if name.endswith('/ExcelRenderer.Fonts.dll')]
        assert len(tool_fonts) == 1, tool_fonts
        assert fonts.read(font_dll) == tool.read(tool_fonts[0]), 'CLI must bundle the same Fonts assembly'
        deps = json.loads(tool.read(next(name for name in tool.namelist() if name.endswith('/ExcelRenderer.Tool.deps.json'))))
        assert any(name.split('/')[0] == 'ExcelRenderer.Fonts' for name in deps['libraries']), deps['libraries']

    parent.mkdir(parents=True, exist_ok=True)
    work = Path(tempfile.mkdtemp(prefix='font-split-', dir=parent))
    # Fresh caches prevent a previous package with the same version from satisfying this check.
    env = dict(os.environ, NUGET_PACKAGES=str(work / 'packages'))
    runtimes = subprocess.check_output(['dotnet', '--list-runtimes'], text=True).splitlines()
    runtime_path = next(line.rsplit('[', 1)[1].rstrip(']') for line in runtimes if line.startswith('Microsoft.NETCore.App 10.'))
    env['DOTNET_ROOT'] = str(Path(runtime_path).parents[1])
    config = ET.Element('configuration')
    sources = ET.SubElement(config, 'packageSources')
    ET.SubElement(sources, 'clear')
    ET.SubElement(sources, 'add', key='local', value=str(packages))
    ET.SubElement(sources, 'add', key='nuget', value='https://api.nuget.org/v3/index.json')
    mapping = ET.SubElement(config, 'packageSourceMapping')
    for source, pattern in [('local', 'ExcelRenderer*'), ('nuget', '*')]:
        ET.SubElement(ET.SubElement(mapping, 'packageSource', key=source), 'package', pattern=pattern)
    config_path = work / 'NuGet.Config'
    ET.ElementTree(config).write(config_path, encoding='utf-8', xml_declaration=True)

    def run(*command):
        print('+', *map(str, command), flush=True)
        subprocess.run(list(map(str, command)), cwd=work, env=env, check=True)

    programs = {
        'without-fonts': '''using ExcelRenderer.Fonts;
var manager = new FontManager(new FontOptions { AllowSystemFonts = false, FallbackFamilies = [] });
try { manager.Resolve(new FontRequest("Noto Sans JP")); throw new Exception("Unexpected bundled font"); }
catch (InvalidOperationException ex) when (ex.Message.StartsWith("No usable font")) { }
''',
        'with-fonts': '''using ExcelRenderer.Fonts;
var manager = new FontManager(new FontOptions { AllowSystemFonts = false, FallbackFamilies = [] });
foreach (var family in new[] { "Noto Sans JP", "Noto Color Emoji", "IPAmjMincho" })
{
    var font = manager.Resolve(new FontRequest(family));
    if (font.Family != family || font.FontData is not { Length: > 0 })
        throw new Exception($"Missing embedded font: {family}");
}
''',
    }
    for name, program in programs.items():
        directory = work / name
        directory.mkdir()
        project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
        properties = ET.SubElement(project, 'PropertyGroup')
        for key, value in [('OutputType', 'Exe'), ('TargetFramework', 'net10.0'), ('ImplicitUsings', 'enable')]:
            ET.SubElement(properties, key).text = value
        references = ET.SubElement(project, 'ItemGroup')
        ET.SubElement(references, 'PackageReference', Include='ExcelRenderer', Version=version)
        if name == 'with-fonts':
            ET.SubElement(references, 'PackageReference', Include='ExcelRenderer.Fonts', Version=version)
        path = directory / 'Consumer.csproj'
        ET.ElementTree(project).write(path, encoding='utf-8', xml_declaration=True)
        (directory / 'Program.cs').write_text(program)
        run('dotnet', 'restore', path, '--configfile', config_path)
        run('dotnet', 'run', '--project', path, '-c', 'Release', '--no-restore')
    run('dotnet', 'tool', 'install', 'ExcelRenderer.Tool', '--version', version,
        '--tool-path', work / 'tool', '--configfile', config_path)
    run(work / 'tool' / ('excelrenderer.exe' if os.name == 'nt' else 'excelrenderer'), '--help')
    print('Font package split passed: no implicit fonts, all three packaged families, identical CLI fonts and installed CLI')


if __name__ == '__main__':
    main()
