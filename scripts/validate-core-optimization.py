#!/usr/bin/env python3
"""Compare saved optimization runtimes using the existing API snapshot and one rasterizer."""
import hashlib
import importlib.util
import json
import pathlib
import shutil
import subprocess
import tempfile

import fitz

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUTPUT = ROOT/'docs/validation/core-performance-optimization'
RUNTIMES = ROOT/'artifacts/core-performance'
FONT = ROOT/'third_party/NotoSansJP/NotoSansJP-Regular.ttf'
VISUAL = RUNTIMES/'output-comparison'
VISUAL.mkdir(exist_ok=True)

def run(*command):
    return subprocess.check_output(list(map(str, command)), cwd=ROOT, text=True, stderr=subprocess.STDOUT)

def normalize(value):
    if isinstance(value, dict):
        return {key: normalize(item) for key, item in value.items() if key not in {'xref', 'seqno'}}
    if isinstance(value, (tuple, list)):
        return [normalize(item) for item in value]
    if isinstance(value, (str, int, float, bool)) or value is None:
        return value
    return list(value)

def compare_pdf(first, second):
    raster_hashes = []
    font_hashes = []
    with fitz.open(first) as old, fitz.open(second) as new:
        assert len(old) == len(new), (first, 'page count')
        for document in [old, new]:
            references = {font[0] for page in document for font in page.get_fonts(full=True)}
            font_hashes.append(sorted(hashlib.sha256(document.extract_font(xref)[3]).hexdigest() for xref in references))
        assert font_hashes[0] == font_hashes[1], (first, 'embedded font subsets')
        for index, (a, b) in enumerate(zip(old, new)):
            assert a.rect == b.rect, (first, index, 'size')
            for method in ['get_text', 'get_drawings', 'get_image_info', 'get_links']:
                arguments = ['rawdict'] if method == 'get_text' else []
                x = normalize(getattr(a, method)(*arguments))
                y = normalize(getattr(b, method)(*arguments))
                assert x == y, (first, index, method)
            # The same installed MuPDF is used for both outputs, at 96 dpi.
            x, y = a.get_pixmap(dpi=96), b.get_pixmap(dpi=96)
            assert (x.width, x.height, x.samples) == (y.width, y.height, y.samples), (first, index, 'pixels')
            raster_hashes.append(hashlib.sha256(x.samples).hexdigest())
    return {'pages': len(raster_hashes), 'structure_equal': True, 'pixel_identical': True, 'raster_sha256': raster_hashes, 'embedded_font_sha256': font_hashes[0]}

report = {'rasterizer': fitz.__doc__, 'dpi': 96, 'core': [], 'main': [], 'native_outputs': []}
for source in sorted((OUTPUT/'inputs').glob('*.xlsx')):
    for phase in ['phase1', 'phase2', 'phase3', 'phase4', 'phase5']:
        entry = compare_pdf(RUNTIMES/'baseline'/(source.stem+'.pdf'), RUNTIMES/phase/(source.stem+'.pdf'))
        report['core'].append({'case': source.stem, 'phase': phase, 'input_sha256': hashlib.sha256(source.read_bytes()).hexdigest(), **entry})
    print('Core', source.stem, 'all phases equal', flush=True)
for source in sorted((ROOT/'tests/ExcelRenderer.Tests/SampleInputs').glob('*.xlsx')):
    normalized = VISUAL/source.name
    run('dotnet', RUNTIMES/'phase5/ExcelRenderer.Core.Validation.dll', 'normalize', source, normalized)
    for phase in ['baseline', 'phase5']:
        run('dotnet', RUNTIMES/phase/'ExcelRenderer.Core.Validation.dll', 'render', normalized, VISUAL/(source.stem+'-core-'+phase+'.pdf'), FONT, 1)
    normalized_inputs = OUTPUT/'normalized-inputs'
    normalized_inputs.mkdir(exist_ok=True)
    shutil.copy2(normalized, normalized_inputs/source.name)
    report['core'].append({'case': source.name, 'input_sha256': hashlib.sha256(normalized.read_bytes()).hexdigest(), **compare_pdf(VISUAL/(source.stem+'-core-baseline.pdf'), VISUAL/(source.stem+'-core-phase5.pdf'))})
    for phase, tool in [('baseline', RUNTIMES/'baseline-tool/ExcelRenderer.Tool.dll'), ('phase5', RUNTIMES/'final-tool/ExcelRenderer.Tool.dll')]:
        run('dotnet', tool, 'render', source, '--format', 'pdf', '-o', VISUAL/(source.stem+'-main-'+phase+'.pdf'), '--font-file', FONT, '--no-system-fonts')
    report['main'].append({'case': source.name, 'input_sha256': hashlib.sha256(source.read_bytes()).hexdigest(), **compare_pdf(VISUAL/(source.stem+'-main-baseline.pdf'), VISUAL/(source.stem+'-main-phase5.pdf'))})
    print('Samples', source.name, 'Core/main equal', flush=True)
for name in ['04-text-decoration', '05-borders', '06-layout-and-pagination', '02-image']:
    source = ROOT/'tests/ExcelRenderer.Tests/SampleInputs'/(name+'.xlsx')
    for fmt in ['png', 'svg']:
        destinations = []
        for phase, tool in [('baseline', RUNTIMES/'baseline-tool/ExcelRenderer.Tool.dll'), ('phase5', RUNTIMES/'final-tool/ExcelRenderer.Tool.dll')]:
            destination = VISUAL/(name+'-'+fmt+'-'+phase)
            destinations.append(destination)
            run('dotnet', tool, 'render', source, '--format', fmt, '-o', destination, '--font-file', FONT, '--no-system-fonts')
        files = [sorted(path.relative_to(directory) for path in directory.rglob('*') if path.is_file()) for directory in destinations]
        assert files[0], (name, fmt, 'no output files')
        assert files[0] == files[1], (name, fmt)
        for path in files[0]:
            assert (destinations[0]/path).read_bytes() == (destinations[1]/path).read_bytes(), (name, fmt, path)
        report['native_outputs'].append({'case': name, 'format': fmt, 'files': len(files[0]), 'bytes_equal': True})
(OUTPUT/'output-comparison.json').write_text(json.dumps(report, indent=2)+'\n')

# Reuse the repository's semantic public/protected/nullable snapshot implementation.
spec = importlib.util.spec_from_file_location('api_snapshot', ROOT/'scripts/compare-core-api.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
with tempfile.TemporaryDirectory(prefix='core-api-') as temporary:
    directory = pathlib.Path(temporary)
    project = directory/'snapshot.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>')
    (directory/'Program.cs').write_text(module.SNAPSHOT_SOURCE)
    run('dotnet', 'restore', project, '--ignore-failed-sources', '-p:NuGetAudit=false')
    run('dotnet', 'build', project, '-c', 'Release', '--no-restore', '-m:1')
    tool = directory/'bin/Release/net10.0/snapshot.dll'
    results = {}
    for product, old, new in [
        ('Core', RUNTIMES/'baseline/ExcelRenderer.Core.dll', RUNTIMES/'phase5/ExcelRenderer.Core.dll'),
        ('main', RUNTIMES/'baseline-main/ExcelRenderer.dll', RUNTIMES/'final-main/ExcelRenderer.dll')]:
        before = run('dotnet', tool, old, RUNTIMES/'baseline-tool')
        after = run('dotnet', tool, new, RUNTIMES/'final-tool')
        (OUTPUT/(product+'-api-before.txt')).write_text(before)
        (OUTPUT/(product+'-api-after.txt')).write_text(after)
        assert before == after, (product, 'public API')
        results[product] = {'equal': True, 'entries': len(before.splitlines())}
    (OUTPUT/'api-comparison.json').write_text(json.dumps(results, indent=2)+'\n')
