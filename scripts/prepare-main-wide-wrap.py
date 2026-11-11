#!/usr/bin/env python3
"""Retain an additional ordinary long-text fixture with Excel's maximum source column width."""
import argparse
import json
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
NS = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
ET.register_namespace('x', NS)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    work = args.directory.resolve()
    inputs = work / 'inputs'
    inputs.mkdir(parents=True, exist_ok=True)
    target = inputs / 'wide-wrap.xlsx'
    if not target.exists():
        with ZipFile(ROOT / 'docs/validation/core-performance-optimization/inputs/wrap1000.xlsx') as archive:
            parts = {name: archive.read(name) for name in archive.namelist()}
        sheet = ET.fromstring(parts['xl/worksheets/sheet1.xml'])
        data = sheet.find('x:sheetData', {'x': NS})
        rows = list(data)[:20]
        data.clear()
        for index, row in enumerate(rows):
            cell = next(iter(row))
            row.clear()
            row.set('r', str(index + 1))
            row.set('ht', '20')
            row.set('customHeight', '1')
            cell.set('r', 'A' + str(index + 1))
            text = cell.find('x:is/x:t', {'x': NS})
            text.text = str(index) + ' ' + ('ABC 日本語 e\u0301 0123 ' * 200)
            row.append(cell)
            data.append(row)
        columns = sheet.find('x:cols', {'x': NS})
        columns.clear()
        ET.SubElement(columns, '{' + NS + '}col', min='1', max='1', width='255', customWidth='1')
        workbook = ET.fromstring(parts['xl/workbook.xml'])
        for name in workbook.findall('x:definedNames/x:definedName', {'x': NS}):
            if name.get('name') == '_xlnm.Print_Area':
                name.text = "'Report'!$A$1:$A$20"
        parts['xl/workbook.xml'] = ET.tostring(workbook, encoding='utf-8', xml_declaration=True)
        parts['xl/worksheets/sheet1.xml'] = ET.tostring(sheet, encoding='utf-8', xml_declaration=True)
        with ZipFile(target, 'w', ZIP_DEFLATED) as archive:
            for name, data in parts.items():
                archive.writestr(name, data)
    (work / 'cases.json').write_text(json.dumps([dict(Name='wide-wrap-Pdf', Input=str(target), Format='Pdf')], indent=2) + '\n')


if __name__ == '__main__':
    main()
