from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED
ns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'
rels='http://schemas.openxmlformats.org/officeDocument/2006/relationships'
files={
'[Content_Types].xml':'<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>'+''.join(f'<Override PartName="/xl/worksheets/sheet{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>' for i in range(1,6))+'</Types>',
'_rels/.rels':f'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="{rels}/officeDocument" Target="xl/workbook.xml"/></Relationships>',
'xl/workbook.xml':f'<workbook xmlns="{ns}" xmlns:r="{rels}"><sheets>'+''.join(f'<sheet name="{name}" sheetId="{i}" r:id="rId{i}"/>' for i,name in enumerate(['Double','Colors','Merged','Defaults','Conditional'],1))+'</sheets></workbook>',
'xl/_rels/workbook.xml.rels':'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'+''.join(f'<Relationship Id="rId{i}" Type="{rels}/worksheet" Target="worksheets/sheet{i}.xml"/>' for i in range(1,6))+f'<Relationship Id="styles" Type="{rels}/styles" Target="styles.xml"/></Relationships>'}
borders=['<border/>','<border><right style="double"/></border>','<border><left style="double"/></border>','<border><bottom style="double"/></border>','<border><top style="double"/></border>','<border><right style="thin"><color rgb="FFFFFFFF"/></right></border>','<border><left style="thin"><color rgb="FF000000"/></left></border>','<border><left style="thin"/><top style="thin"/></border>','<border><right style="thin"/><bottom style="thin"/></border>','<border><right style="thick"/></border>']
files['xl/styles.xml']=f'<styleSheet xmlns="{ns}"><fonts count="1"><font><sz val="11"/><name val="Noto Sans JP"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="10">'+''.join(borders)+'</borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="11">'+''.join(f'<xf numFmtId="0" fontId="0" fillId="0" borderId="{i}" xfId="0" applyBorder="1"/>' for i in [0,1,2,3,4,5,6,7,8,9,2])+'</cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles><dxfs count="1"><dxf><border><right/></border></dxf></dxfs></styleSheet>'
def cell(a,s,value=None):return f'<c r="{a}" s="{s}">'+(f'<v>{value}</v>' if value is not None else '')+'</c>'
def row(i,c,attrs=''):return f'<row r="{i}" ht="30" customHeight="1" {attrs}>{c}</row>'
def sheet(rows,extra='',cols='<col min="1" max="4" width="8" customWidth="1"/>'):return f'<worksheet xmlns="{ns}"><sheetFormatPr defaultRowHeight="30"/><cols>{cols}</cols><sheetData>{rows}</sheetData>{extra}<pageMargins left="0.25" right="0.25" top="0.25" bottom="0.25" header="0" footer="0"/></worksheet>'
files['xl/worksheets/sheet1.xml']=sheet(row(1,cell('A1',1)+cell('B1',2))+row(3,cell('A3',3))+row(4,cell('A4',4))+row(6,cell('A6',1)+cell('B6',10)))
files['xl/worksheets/sheet2.xml']=sheet(row(1,cell('A1',5)+cell('B1',6))+row(3,cell('A3',1)+cell('B3',9)))
files['xl/worksheets/sheet3.xml']=sheet(row(1,cell('A1',7)+cell('B1',1))+row(2,cell('B2',8)), '<mergeCells count="1"><mergeCell ref="A1:B2"/></mergeCells>')
files['xl/worksheets/sheet4.xml']=sheet(row(1,'<c r="A1"/>', 's="3" customFormat="1"')+row(2,'<c r="A2"/>', 's="3" customFormat="1"')+row(3,'<c r="A3"/>'),cols='<col min="1" max="1" width="8" customWidth="1" style="1"/>')
files['xl/worksheets/sheet5.xml']=sheet(row(1,cell('A1',9,1)+cell('B1',6)), '<conditionalFormatting sqref="A1"><cfRule type="cellIs" operator="equal" priority="1" dxfId="0"><formula>1</formula></cfRule></conditionalFormatting>')
path=Path('tests/ExcelRenderer.Tests/SampleInputs/BorderResolution/border-conflicts.xlsx')
path.parent.mkdir(parents=True, exist_ok=True)
with ZipFile(path,'w',ZIP_DEFLATED) as z:
 for name,xml in sorted(files.items()):
  info=ZipInfo(name,date_time=(2026,1,1,0,0,0)); info.compress_type=ZIP_DEFLATED
  z.writestr(info,xml.encode())
print(path)
