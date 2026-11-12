#!/usr/bin/env python3
"""Generate deterministic performance inputs: short cells and long sparse rows."""
from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED

ROOT = Path(__file__).resolve().parent
NS = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'


def create(name, rows):
    parts = {
        '[Content_Types].xml': '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>',
        '_rels/.rels': '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>',
        'xl/workbook.xml': f'<workbook xmlns="{NS}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="overflow" sheetId="1" r:id="rId1"/></sheets></workbook>',
        'xl/_rels/workbook.xml.rels': '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>',
        'xl/worksheets/sheet1.xml': f'<worksheet xmlns="{NS}"><sheetPr><pageSetUpPr fitToPage="1"/></sheetPr><sheetData>{rows}</sheetData><pageSetup paperSize="9" fitToWidth="1" fitToHeight="1"/></worksheet>',
    }
    with ZipFile(ROOT / name, 'w', ZIP_DEFLATED) as archive:
        for path, text in parts.items():
            info = ZipInfo(path, (2026, 11, 12, 0, 0, 0))
            info.compress_type = ZIP_DEFLATED
            archive.writestr(info, text)


create('short.xlsx', ''.join(f'<row r="{r}">' + ''.join(f'<c r="{c}{r}" t="inlineStr"><is><t>x</t></is></c>' for c in 'ABCDEFGHIJ') + '</row>' for r in range(1, 501)))
create('sparse.xlsx', ''.join(f'<row r="{r}"><c r="A{r}" t="inlineStr"><is><t>{"W" * 100}</t></is></c><c r="WQJ{r}" t="inlineStr"><is><t>block</t></is></c></row>' for r in range(1, 51)))
