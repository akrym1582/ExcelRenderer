#!/usr/bin/env python3
"""Validate saved native captures, font subsets and observation-only baseline counters."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

import fitz

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def artifact_hash(path):
    if path.suffix != '.svg':
        return sha(path)
    root = ET.fromstring(path.read_bytes())
    names = {node.attrib['id']: 'resource' + str(index) for index, node in enumerate(node for node in root.iter() if 'id' in node.attrib)}
    for node in root.iter():
        for key, value in list(node.attrib.items()):
            if key == 'id':
                node.attrib[key] = names[value]
            elif value.startswith('url(#') and value.endswith(')') and value[5:-1] in names:
                node.attrib[key] = 'url(#' + names[value[5:-1]] + ')'
            elif value.startswith('#') and value[1:] in names:
                node.attrib[key] = '#' + names[value[1:]]
    return hashlib.sha256(ET.tostring(root)).hexdigest()


def normalize(value):
    if isinstance(value, dict):
        return {key: normalize(item) for key, item in value.items() if key not in ('xref', 'seqno', 'id')}
    if isinstance(value, (list, tuple)):
        return [normalize(item) for item in value]
    if value is None or isinstance(value, (str, int, float, bool)):
        return value
    return list(value)


def pdf(first, second):
    fonts = []
    with fitz.open(first) as a, fitz.open(second) as b:
        assert len(a) == len(b), (first, 'page count')
        for document in (a, b):
            refs = {font[0] for page in document for font in page.get_fonts(full=True)}
            fonts.append(sorted(hashlib.sha256(document.extract_font(xref)[3]).hexdigest() for xref in refs))
        assert fonts[0] == fonts[1], (first, 'embedded font subsets')
        for old, new in zip(a, b):
            assert old.rect == new.rect
            for method in ('get_texttrace', 'get_drawings', 'get_image_info', 'get_links'):
                assert normalize(getattr(old, method)()) == normalize(getattr(new, method)()), (first, method)
            x, y = old.get_pixmap(dpi=96), new.get_pixmap(dpi=96)
            assert (x.width, x.height, x.samples) == (y.width, y.height, y.samples), (first, 'pixels')
        return dict(pages=len(a), structureEqual=True, pixelIdentical=True, embeddedFontSha256=fonts[0])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    work = args.directory.resolve()
    report = dict(pdf=[], native=[], core=[], baselineObservations=[])
    for case in sorted((work / 'captures/before').iterdir()):
        for source in sorted(case.iterdir()):
            target = work / 'captures/after' / case.name / source.name
            if source.suffix == '.pdf':
                report['pdf'].append(dict(case=case.name, file=source.name, **pdf(source, target)))
            else:
                assert artifact_hash(source) == artifact_hash(target), source
                report['native'].append(dict(case=case.name, file=source.name, canonicalBytesEqual=True))
    captures = work / 'observation-captures'
    for name in ('text1000-Pdf', 'wrap1000-Pdf', 'borders1000-Pdf', 'rich-Pdf', '05-borders-Png'):
        case = next(case for case in json.loads((work / 'cases.json').read_text()) if case['Name'] == name)
        case['Capture'] = str(captures / name)
        specification = work / 'observation-case.json'
        specification.write_text(json.dumps([case]))
        command = ['dotnet', str(work / 'observation-before/ExcelRenderer.Performance.dll'), 'render-profile', str(specification), name, '1', '1', 'diagnostic']
        result = subprocess.check_output(command, cwd=ROOT, text=True)
        records = [json.loads(line) for line in result.splitlines()]
        observed = next(record for record in records[1:] if record['diagnostic'])
        original = next(record for record in records[1:] if record['observerControl'])
        assert observed['captureHash'] == original['captureHash']
        for path in (captures / name).iterdir():
            assert artifact_hash(path) == artifact_hash(work / 'captures/before' / name / path.name), (name, 'baseline observer changed bytes')
        report['baselineObservations'].append(dict(case=name, command=command, records=records, outputEqualToPrimary=True))
    font = ROOT / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'
    core = work / 'core-comparison'
    core.mkdir(exist_ok=True)
    for name in ('text1', 'text1000', 'wrap1000', 'borders1000', 'header', 'imagesonly', 'empty'):
        paths = []
        for stage in ('before', 'after'):
            target = core / (name + '-' + stage + '.pdf')
            subprocess.run(['dotnet', str(work / ('core-' + stage) / 'ExcelRenderer.Core.Validation.dll'), 'render', str(work / 'inputs' / (name + '.xlsx')), str(target), str(font), '1'], cwd=ROOT, check=True, capture_output=True)
            paths.append(target)
        report['core'].append(dict(case=name, **pdf(*paths)))
    (work / 'structural-quality.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({key: len(value) for key, value in report.items()}))


if __name__ == '__main__':
    main()
