#!/usr/bin/env python3
"""Generate Phase 3 XLSX fixtures using only the Python standard library."""
import argparse
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

MAIN = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
X14 = 'http://schemas.microsoft.com/office/spreadsheetml/2009/9/main'
XM = 'http://schemas.microsoft.com/office/excel/2006/main'
LINK = '{A3CBA125-C045-4C79-BDA7-A07FDC89F058}'
SETS = ('3Arrows', '3ArrowsGray', '3TrafficLights1', '3Symbols', '3Symbols2')


def write(path, rows, rules, extra='', rtl=False, columns=5, extension_rules=''):
    parts = {
        '[Content_Types].xml': '''<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>''',
        '_rels/.rels': '''<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>''',
        'xl/workbook.xml': f'<workbook xmlns="{MAIN}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Phase3" sheetId="1" r:id="rId1"/></sheets></workbook>',
        'xl/_rels/workbook.xml.rels': '''<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>''',
        'xl/styles.xml': f'<styleSheet xmlns="{MAIN}"><fonts count="1"><font><sz val="11"/><name val="Noto Sans JP"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/></border></borders><cellStyleXfs count="1"><xf/></cellStyleXfs><cellXfs count="1"><xf xfId="0" fontId="0" fillId="0" borderId="0" numFmtId="0"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>',
        'xl/worksheets/sheet1.xml': f'<worksheet xmlns="{MAIN}" xmlns:x14="{X14}" xmlns:xm="{XM}"><sheetViews><sheetView workbookViewId="0" rightToLeft="{int(rtl)}"/></sheetViews><sheetFormatPr defaultRowHeight="24"/><cols><col min="1" max="{columns}" width="18" customWidth="1"/></cols><sheetData>{rows}</sheetData>{extra}{rules}<pageMargins left="0.3" right="0.3" top="0.3" bottom="0.3" header="0.1" footer="0.1"/>{extension_rules}</worksheet>',
    }
    with ZipFile(path, 'w', ZIP_DEFLATED) as archive:
        for name, data in parts.items():
            archive.writestr(name, data)


def rows(values, columns=1):
    return ''.join(f'<row r="{i}" ht="24" customHeight="1">' + ''.join(f'<c r="{chr(65+c)}{i}"><v>{v}</v></c>' for c in range(columns)) + '</row>' for i, v in enumerate(values, 1))


def legacy_bar(reference, priority=1, show=True, linked=False):
    link = f'<extLst><ext uri="{{B025F937-C7B1-47D3-B67F-A62EFF666E3E}}"><x14:id>{LINK}</x14:id></ext></extLst>' if linked else ''
    return f'<conditionalFormatting sqref="{reference}"><cfRule type="dataBar" priority="{priority}"><dataBar showValue="{int(show)}"><cfvo type="num" val="0"/><cfvo type="num" val="100"/><color rgb="FF0080FF"/></dataBar>{link}</cfRule></conditionalFormatting>'


def extended_bar(reference, axis, priority=1, linked=False, direction='context'):
    priority_attr = '' if linked else f'priority="{priority}"'
    fill = '' if linked else '<x14:fillColor rgb="FF0080FF"/>'
    return f'<x14:conditionalFormatting><x14:cfRule type="dataBar" {priority_attr} id="{LINK}"><x14:dataBar minLength="0" maxLength="100" gradient="0" border="1" axisPosition="{axis}" direction="{direction}"><x14:cfvo type="num"><xm:f>-50</xm:f></x14:cfvo><x14:cfvo type="num"><xm:f>100</xm:f></x14:cfvo>{fill}<x14:borderColor rgb="FF004080"/><x14:negativeFillColor rgb="FFC0504D"/><x14:axisColor rgb="FF000000"/></x14:dataBar></x14:cfRule><xm:sqref>{reference}</xm:sqref></x14:conditionalFormatting>'


def extensions(body):
    return '<extLst><ext uri="{78C0D931-6437-407d-A8EE-F0AAD7539E65}"><x14:conditionalFormattings>' + body + '</x14:conditionalFormattings></ext></extLst>'


def icons(reference, name, priority=1, show=True):
    return f'<conditionalFormatting sqref="{reference}"><cfRule type="iconSet" priority="{priority}"><iconSet iconSet="{name}" showValue="{int(show)}"><cfvo type="num" val="0"/><cfvo type="num" val="33"/><cfvo type="num" val="67"/></iconSet></cfRule></conditionalFormatting>'


def generate(destination):
    destination.mkdir(parents=True, exist_ok=True)
    write(destination / 'legacy-bars.xlsx', rows([0, 25, 50, 75, 100], 2), legacy_bar('A1:A5') + legacy_bar('B1:B5', 2, False), columns=2)
    write(destination / 'x14-bars.xlsx', rows([-50, -25, 0, 25, 50, 100], 4), legacy_bar('A1:A6', linked=True), columns=4, extension_rules=extensions(extended_bar('A1:A6', 'automatic', linked=True) + extended_bar('B1:B6', 'middle', 2) + extended_bar('C1:C6', 'none', 3) + extended_bar('D1:D6', 'automatic', 4, direction='rightToLeft')))
    write(destination / 'icons.xlsx', rows([0, 33, 67], 5), ''.join(icons(f'{chr(65+i)}1:{chr(65+i)}3', name, i+1) for i, name in enumerate(SETS)))
    layout_rows = '<row r="1" ht="32" customHeight="1"><c r="A1" t="inlineStr"><is><r><rPr><b/></rPr><t>Rich </t></r><r><t>text</t></r></is></c></row><row r="2" ht="28" customHeight="1"><c r="A2" t="inlineStr"><is><t>**items[*]</t></is></c></row>'
    write(destination / 'layout-mapping.xlsx', layout_rows, legacy_bar('A2', linked=True) + icons('A2', '3Arrows', 2, False), '<mergeCells count="1"><mergeCell ref="A1:C1"/></mergeCells>', columns=3, extension_rules=extensions(extended_bar('A2', 'automatic', linked=True)))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'docs/validation/conditional-formatting-phase3/fixtures')
    generate(parser.parse_args().output)
