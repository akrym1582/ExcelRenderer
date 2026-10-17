#!/usr/bin/env python3
"""Reproduce the 1.9.0 -> 1.9.1 break using released NuGet artifacts, not source builds."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import urllib.request
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape
import zipfile

ROOT = Path(__file__).resolve().parents[1]
FEED = 'https://api.nuget.org/v3/index.json'
VERSIONS = ('1.9.0', '1.9.1')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work', required=True, type=Path, help='New or empty output directory')
    parser.add_argument('--downloads', type=Path, help='Reusable nupkg download cache')
    args = parser.parse_args()
    work = args.work.resolve()
    if work.exists() and any(work.iterdir()):
        parser.error('--work must be new or empty')
    work.mkdir(parents=True, exist_ok=True)
    downloads = args.downloads.resolve() if args.downloads else work / 'downloads'
    downloads.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, NUGET_PACKAGES=str(work / 'packages'))
    config = work / 'NuGet.Config'
    config.write_text(f'<configuration><packageSources><clear/><add key="nuget.org" value="{FEED}"/></packageSources></configuration>')
    report = {'feed': FEED, 'packages': {}, 'checks': {}, 'api': {}}

    def run(name, command, *, success=True):
        result = subprocess.run(list(map(str, command)), cwd=work, env=env,
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (work / f'{name}.log').write_text(result.stdout)
        assert (result.returncode == 0) == success, (name, result.returncode, result.stdout)
        report['checks'][name] = {'exit_code': result.returncode, 'log': f'{name}.log'}
        print(f'{name}: expected {"success" if success else "failure"}', flush=True)
        return result.stdout

    def project(directory, references=''):
        directory.mkdir(parents=True, exist_ok=True)
        path = directory / 'Consumer.csproj'
        path.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                        '<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
                        '<Nullable>enable</Nullable></PropertyGroup><ItemGroup>' + references + '</ItemGroup></Project>')
        return path

    def package_refs(names, version):
        return ''.join(f'<PackageReference Include="{name}" Version="[{version}]"/>' for name in names)

    # The library packages in 1.9.0 omit these DLLs. The installed CLI payload is
    # the released baseline for type identity, not a successful library restore.
    for version in VERSIONS:
        for name in ('ExcelRenderer', 'ExcelRenderer.Mapping', 'ExcelRenderer.Tool'):
            filename = f'{name}.{version}.nupkg'
            path = downloads / filename
            if not path.exists():
                url = f'https://api.nuget.org/v3-flatcontainer/{name.lower()}/{version}/{filename.lower()}'
                temporary = path.with_suffix('.download')
                urllib.request.urlretrieve(url, temporary)
                temporary.replace(path)
            with zipfile.ZipFile(path) as archive:
                spec = ET.fromstring(archive.read(next(p for p in archive.namelist() if p.endswith('.nuspec'))))
                dependencies = {n.attrib['id']: n.attrib['version'] for n in spec.iter()
                                if n.tag.rsplit('}', 1)[-1] == 'dependency'}
                dlls = {}
                for member in archive.namelist():
                    if member.endswith(('.dll', '.xml')) and Path(member).name.startswith('ExcelRenderer'):
                        target = work / 'artifacts' / version / name / Path(member).name
                        target.parent.mkdir(parents=True, exist_ok=True)
                        data = archive.read(member)
                        target.write_bytes(data)
                        dlls[Path(member).name] = hashlib.sha256(data).hexdigest()
                report['packages'][f'{name}/{version}'] = {
                    'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                    'dependencies': dependencies, 'files': dlls,
                }
    for name in ('ExcelRenderer', 'ExcelRenderer.Mapping'):
        old = report['packages'][f'{name}/1.9.0']
        assert old['dependencies']['ExcelRenderer.OpenXml'] == '1.8.0'
        new = report['packages'][f'{name}/1.9.1']
        assert not {'ExcelRenderer.Formulas', 'ExcelRenderer.OpenXml'} & new['dependencies'].keys()
        for assembly in ('ExcelRenderer.Formulas', 'ExcelRenderer.OpenXml'):
            assert f'{assembly}.dll' not in old['files']
            for extension in ('dll', 'xml'):
                filename = f'{assembly}.{extension}'
                assert new['files'][filename] == report['packages']['ExcelRenderer.Mapping/1.9.1']['files'][filename]
            assert new['files'][f'{assembly}.dll'] == report['packages']['ExcelRenderer.Tool/1.9.1']['files'][f'{assembly}.dll']

    inspector = project(work / 'inspector')
    shutil.copyfile(ROOT / 'scripts/namespace-migration/Inventory.cs', inspector.parent / 'Program.cs')
    run('inspector-build', ['dotnet', 'build', inspector, '-c', 'Release', '--configfile', config])
    for version in VERSIONS:
        paths = sorted((work / 'artifacts' / version / 'ExcelRenderer.Tool').glob('*.dll'))
        inventory = json.loads(run(f'api-{version}', ['dotnet', inspector.parent / 'bin/Release/net10.0/Consumer.dll', *paths]))
        report['api'][version] = {item['assembly']: item for item in inventory}
    changes = {}
    for assembly, old in report['api']['1.9.0'].items():
        new = report['api']['1.9.1'][assembly]
        removed = sorted(set(old['types']) - set(new['types']))
        added = sorted(set(new['types']) - set(old['types']))
        changes[assembly] = {'removed': removed, 'added': added}
    report['type_changes'] = changes
    moves = {}
    new_types = {}
    for assembly in ('ExcelRenderer.Formulas', 'ExcelRenderer.OpenXml'):
        change = changes[assembly]
        by_name = {name.rsplit('.', 1)[-1]: name for name in change['added']}
        moves[assembly] = {old: by_name[old.rsplit('.', 1)[-1]] for old in change['removed']}
        new_types[assembly] = sorted(set(change['added']) - set(moves[assembly].values()))
        assert not report['api']['1.9.1'][assembly]['exportedTypes'], 'Review type forwarding'
    assert len(moves['ExcelRenderer.Formulas']) == 20
    assert len(moves['ExcelRenderer.OpenXml']) == 36
    report['namespace_moves'] = moves
    report['new_types'] = new_types
    guide = (ROOT / 'docs/namespace-migration.md').read_text()
    for assembly in moves:
        section = guide.split(f'## From `{assembly}`\n', 1)[1].split('\n## ', 1)[0]
        documented = {}
        for namespace, type_names in re.findall(r'^\| `([^`]+)` \| (.+) \|$', section, re.MULTILINE):
            for type_name in re.findall(r'`([^`]+)`', type_names):
                metadata_name = type_name.replace('.', '+')
                documented[f'{assembly}.{metadata_name}'] = f'{namespace}.{metadata_name}'
        assert documented == moves[assembly], (assembly, documented, moves[assembly])
    report['checks']['migration-table'] = {'moved_public_types': sum(map(len, moves.values()))}
    for filename in ('README.md', 'README.ja.md'):
        text = (ROOT / filename).read_text()
        table = text.split('| --- | --- | --- |', 1)[1].split('\n\n', 1)[0]
        names = re.findall(r'^\| `([^`]+)`', table, re.MULTILINE)
        assert set(names) == {'ExcelRenderer', 'ExcelRenderer.Mapping', 'ExcelRenderer.Tool', 'ExcelRenderer.Fonts'}, names
    report['checks']['readme-package-tables'] = {'published_package_count': 4}

    sample = (ROOT / 'samples/namespace-migration/Program.cs').read_text()
    legacy_lines = []
    for line in sample.splitlines():
        if line.startswith('using ExcelRenderer.Formulas.'):
            line = 'using ExcelRenderer.Formulas;'
        elif line.startswith('using ExcelRenderer.OpenXml.'):
            line = 'using ExcelRenderer.OpenXml;'
        if line not in legacy_lines or not line.startswith('using '):
            legacy_lines.append(line)
    legacy = '\n'.join(legacy_lines) + '\n'

    # Test ordinary 1.9.0 NuGet consumers separately from the CLI-DLL baseline.
    for name in ('ExcelRenderer', 'ExcelRenderer.Mapping'):
        path = project(work / f'restore-old-{name}', package_refs([name], '1.9.0'))
        output = run(f'restore-old-{name}', ['dotnet', 'restore', path, '--configfile', config], success=False)
        assert 'NU1101' in output and 'ExcelRenderer.OpenXml' in output, output

    # A real old-namespace application can be compiled against the released CLI
    # DLLs. Explicit third-party dependencies are required for this baseline.
    old_dir = work / 'legacy'
    old_refs = ''.join(f'<Reference Include="{name}"><HintPath>{escape(str(work / "artifacts/1.9.0/ExcelRenderer.Tool" / (name + ".dll")))}</HintPath></Reference>'
                       for name in ('ExcelRenderer.Formulas', 'ExcelRenderer.OpenXml'))
    path = project(old_dir, old_refs + package_refs(['DocumentFormat.OpenXml'], '3.3.0') + package_refs(['ExcelNumberFormat'], '1.1.0'))
    (old_dir / 'Program.cs').write_text(legacy)
    run('legacy-build', ['dotnet', 'build', path, '-c', 'Release', '--configfile', config])
    old_output = old_dir / 'bin/Release/net10.0'
    baseline_hash = hashlib.sha256((old_output / 'Consumer.dll').read_bytes()).hexdigest()
    for mode in ('engine', 'reader'):
        run(f'legacy-{mode}', ['dotnet', old_output / 'Consumer.dll', mode])

    for label, names in (('main', ['ExcelRenderer']), ('mapping', ['ExcelRenderer.Mapping']),
                         ('combined', ['ExcelRenderer', 'ExcelRenderer.Mapping'])):
        directory = work / label
        path = project(directory, package_refs(names, '1.9.1'))
        (directory / 'Program.cs').write_text(legacy)
        errors = run(f'{label}-old-source', ['dotnet', 'build', path, '-c', 'Release', '--configfile', config], success=False)
        assert 'CS0246' in errors and 'FormulaEngine' in errors and 'OpenXmlWorkbookReader' in errors, errors
        (directory / 'Program.cs').write_text(sample)
        run(f'{label}-migrated-build', ['dotnet', 'build', path, '-c', 'Release', '--no-restore'])
        output = directory / 'bin/Release/net10.0'
        run(f'{label}-migrated-run', ['dotnet', output / 'Consumer.dll'])
        # Preserve the old executable/deps/runtimeconfig byte-for-byte, replacing
        # only the two product DLLs to test CLR type resolution independently.
        binary = work / f'{label}-binary-swap'
        shutil.copytree(old_output, binary)
        for name in ('ExcelRenderer.Formulas', 'ExcelRenderer.OpenXml'):
            shutil.copyfile(output / f'{name}.dll', binary / f'{name}.dll')
        assert hashlib.sha256((binary / 'Consumer.dll').read_bytes()).hexdigest() == baseline_hash
        report['checks'][f'{label}-unchanged-binary'] = {'consumer_sha256': baseline_hash}
        for mode, type_name in (('engine', 'ExcelRenderer.Formulas.FormulaEngine'),
                                ('reader', 'ExcelRenderer.OpenXml.OpenXmlWorkbookReader')):
            failure = run(f'{label}-binary-{mode}', ['dotnet', binary / 'Consumer.dll', mode], success=False)
            assert 'TypeLoadException' in failure and type_name in failure, failure

    (work / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    print(f'Published package compatibility checks passed; evidence: {work / "report.json"}')


if __name__ == '__main__':
    main()
