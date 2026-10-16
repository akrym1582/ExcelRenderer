#!/usr/bin/env python3
"""Enforce BCL calculation core and adapter dependency direction."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
ENGINE = ROOT / 'src/ExcelRenderer.Formulas'
project = ET.parse(ENGINE / 'ExcelRenderer.Formulas.csproj').getroot()
assert not project.findall('.//ProjectReference'), 'Formulas must not depend on product adapters'
assert not project.findall('.//PackageReference'), 'Formulas runtime must use only the BCL'
assert project.findtext('.//TargetFramework') == 'netstandard2.1'
assert project.findtext('.//IsPackable') == 'true', 'Integrated packages must resolve Formulas through its package'
for path in ENGINE.rglob('*.cs'):
    if {'bin', 'obj'} & set(path.parts):
        continue
    code = re.sub(r'^\s*///.*$', '', path.read_text(), flags=re.MULTILINE)
    assert not re.search(r'\b(ClosedXML|NPOI|ANTLR|DocumentFormat|PDFsharp|SkiaSharp)\b|ExcelRenderer\.(Core|Mapping|OpenXml|Fonts)', code), path
    assert 'DataTable.Compute' not in code, path
for path in (ROOT / 'src').glob('*/*.csproj'):
    xml = ET.parse(path).getroot()
    packages = {p.attrib['Include'] for p in xml.findall('.//PackageReference')}
    assert not any(p.startswith(('ClosedXML', 'NPOI', 'Antlr')) for p in packages), (path, packages)
    for ref in xml.findall('.//ProjectReference'):
        if 'ExcelRenderer.Formulas' in ref.attrib['Include']:
            assert path.parent.name in {'ExcelRenderer.OpenXml', 'ExcelRenderer.Mapping', 'ExcelRenderer.Core', 'ExcelRenderer'}, path
print('BCL formula engine, integrated package dependency and adapter direction passed')
