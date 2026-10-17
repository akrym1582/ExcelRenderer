#!/usr/bin/env python3
"""Check dependency direction and shared reader/layout/drawing/PDF ownership."""
import pathlib
import re
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
CORE = ROOT / 'src/ExcelRenderer.Core'
project = ET.parse(CORE / 'ExcelRenderer.Core.csproj').getroot()
assert {p.attrib['Include'] for p in project.findall('.//ProjectReference')} == {'../ExcelRenderer.OpenXml/ExcelRenderer.OpenXml.csproj'}, 'Core may reference only shared OpenXml'
for path in CORE.rglob('*.cs'):
    if {'bin', 'obj'} & set(path.parts):
        continue
    for name in re.findall(r'\bExcelRenderer\.[A-Za-z0-9_.]+', path.read_text()):
        assert name.startswith('ExcelRenderer.Core') or name.startswith('ExcelRenderer.OpenXml') or name == 'ExcelRenderer.Tests', f'{path}: forbidden main namespace {name}'
for relative, owner in {
    'Excel/CellRangeIndex.cs': 'Core.Excel.CoreCellRangeIndex',
    'Drawing/TextLayoutFontSize.cs': 'Core.Drawing.TextLayoutFontSize',
    'Excel/ColumnWidthCalculator.cs': 'Core.Excel.ColumnWidthCalculator',
    'Excel/ExcelStyleConverter.cs': 'Core.Excel.OpenXmlStyleConverter',
    'Excel/WorkbookLayoutMetadataReader.cs': 'Core.Excel.WorkbookLayoutMetadataReader',
    'Layout/BandIndex.cs': 'Core.Layout.BandIndex',
    'Layout/SheetGeometry.cs': 'Core.Layout.SheetGeometry',
    'Layout/PageBandBuilder.cs': 'Core.Layout.PageBandBuilder',
    'Layout/PagePlacement.cs': 'Core.Layout.PagePlacement',
    'Layout/PrintScaleResolver.cs': 'Core.Layout.PrintScaleResolver',
    'Layout/CellContentBounds.cs': 'Core.Layout.CellContentBounds',
    'Drawing/BorderStrokeGeometry.cs': 'Core.Drawing.BorderStrokeGeometry',
    'Drawing/TextLayoutPlacement.cs': 'Core.Drawing.TextLayoutPlacement',
    'Rendering/WorkbookInputPreparer.cs': 'Core.Input.WorkbookInputPreparer',
    'Rendering/SpillableBufferStream.cs': 'Core.Input.SpillableBufferStream',
}.items():
    text = (ROOT / 'src/ExcelRenderer' / relative).read_text()
    assert owner in text, f'{relative}: lost shared delegation'
    assert not re.search(r'\b(for|foreach|while)\s*\(', text), f'{relative}: algorithm returned to facade'
print('Core dependency direction and migrated helper ownership passed')

reader = (ROOT / 'src/ExcelRenderer/Excel/ExcelReader.cs').read_text()
assert 'FullExcelReader' in reader and 'CellsUsed' not in reader and 'new XLWorkbook' not in reader, 'Main reader must delegate workbook traversal'
assert 'class FullExcelReader : CoreExcelReader' in (ROOT / 'src/ExcelRenderer/CoreIntegration/FullExcelReader.cs').read_text()

for relative, owner in {
    'Layout/ReportLayoutEngine.cs': 'Core.Layout.ReportLayoutEngine',
    'Layout/SheetLayoutPlan.cs': 'Core.Layout.SheetLayoutPlan',
    'Layout/RenderPageBuilder.cs': 'Core.Layout.RenderPageBuilder',
    'Layout/CellBoundsPass.cs': 'Core.Layout.CellBoundsPass',
    'Layout/TextMeasurePass.cs': 'Core.Layout.TextMeasurePass',
    'Layout/PaginationPass.cs': 'Core.Layout.PaginationPass',
    'Layout/NormalizePass.cs': 'Core.Layout.NormalizePass',
    'Layout/HiddenRowColumnPass.cs': 'Core.Layout.HiddenRowColumnPass',
    'Layout/ColumnLayoutPass.cs': 'Core.Layout.ColumnLayoutPass',
    'Layout/RowLayoutPass.cs': 'Core.Layout.RowLayoutPass',
    'Layout/DrawingAnchorResolver.cs': 'Core.Layout.DrawingAnchorResolver',
    'Layout/HeaderFooterLayout.cs': 'Core.Layout.HeaderFooterLayout',
    'Layout/TextLayoutTransform.cs': 'Core.Layout.TextLayoutTransform',
    'Drawing/DrawCommandGeneratorPass.cs': 'Core.Drawing.DrawCommandGeneratorPass',
    'PdfSharp/PdfSharpRenderer.cs': 'Core.Pdf.CorePdfRenderer',
    'Excel/StylePool.cs': 'Core.Excel.StyleInternPool',
    'Rendering/DiagnosticCollector.cs': 'Core.Rendering.DiagnosticStore',
}.items():
    text = (ROOT / 'src/ExcelRenderer' / relative).read_text()
    assert owner in text, f'{relative}: common engine delegation lost'
    assert not re.search(r'(Math\.Sqrt|CreateBands|LastColumnEnd|_scaledBorders|_repeatedRows)', text), f'{relative}: duplicated basic algorithm'
normal = (ROOT / 'src/ExcelRenderer/ExcelConverter.RenderAsync.cs').read_text()
assert 'ReadCore(' in normal and 'BuildCore(' in normal and 'Core.Drawing.DrawCommandGeneratorPass' in normal
assert 'static readonly SemaphoreSlim' not in normal
assert 'class FullLayoutPolicy : Core.Layout.CoreLayoutPolicy' in (ROOT / 'src/ExcelRenderer/CoreIntegration/FullLayoutPolicy.cs').read_text()
print('Shared conversion pipeline and compatibility facade ownership passed')

# XML and bounded workbook input belong to the shared OpenXml project.
OPENXML = ROOT / 'src/ExcelRenderer.OpenXml'
reader_project = ET.parse(OPENXML / 'ExcelRenderer.OpenXml.csproj').getroot()
assert {p.attrib['Include'] for p in reader_project.findall('.//ProjectReference')} == {'../ExcelRenderer.Formulas/ExcelRenderer.Formulas.csproj'}, 'OpenXml may reference only the BCL formula engine'
reader_packages = {p.attrib['Include'] for p in reader_project.findall('.//PackageReference')}
assert reader_packages == {'DocumentFormat.OpenXml', 'ExcelNumberFormat'}, reader_packages
for path in OPENXML.rglob('*.cs'):
    if {'bin', 'obj'} & set(path.parts):
        continue
    text = path.read_text()
    code = re.sub(r'^\s*///.*$', '', text, flags=re.MULTILINE)
    if path.name == 'AssemblyInfo.cs':
        code = re.sub(r'^\[assembly: InternalsVisibleTo\(.*$', '', code, flags=re.MULTILINE)
    assert not re.search(r'\b(ClosedXML|PDFsharp|SkiaSharp)\b|ExcelRenderer\.(Core|Mapping|Fonts)', code), path
    assert 'WorksheetPart.Worksheet' not in text and '.SharedStringTable' not in text.replace('.SharedStringTablePart', ''), path
for name in ('ExcelRenderer.Core', 'ExcelRenderer', 'ExcelRenderer.Mapping'):
    path = ROOT / 'src' / name / (name + '.csproj')
    assert 'ExcelRenderer.OpenXml' in path.read_text(), f'{path}: shared OpenXml reference missing'
for relative in ('Input/SpillableBufferStream.cs', 'Input/WorkbookInputPreparer.cs'):
    text = (CORE / relative).read_text()
    assert 'OpenXml.Input.' in text, f'{relative}: shared input delegation missing'
    assert not re.search(r'\b(for|foreach|while)\s*\(', text), f'{relative}: duplicate input algorithm'
print('Shared OpenXml dependency direction and input ownership passed')

# Product enrichment hooks and the shared mapping language must remain source-neutral.
for path in (ROOT / 'src/ExcelRenderer/CoreIntegration').glob('FullExcelReader*.cs'):
    assert not re.search(r'\b(IXL\w*|XLWorkbook)\b|using ClosedXML', path.read_text()), f'{path}: legacy type returned to product hooks'
assert 'ClosedXML' not in (CORE / 'Excel/ICoreFontMetrics.cs').read_text(), 'Neutral metrics include legacy engine types'
for folder in ('Templates', 'Expansion', 'Expressions'):
    paths = list((ROOT / 'src/ExcelRenderer.Mapping' / folder).glob('*.cs'))
    assert paths, f'{folder}: shared mapping language sources missing'
    for path in paths:
        assert not re.search(r'\b(IXL\w*|XLCellValue|XLWorkbook)\b|using ClosedXML', path.read_text()), f'{path}: language depends on legacy cells'
print('Source-neutral reader hooks and shared mapping language passed')

markdown_geometry = (ROOT / "src/ExcelRenderer/Markdown/VisualCellBuilder.cs").read_text()
assert "Layout.SheetGeometry" in markdown_geometry and "PrefixSums" not in markdown_geometry, "Markdown geometry must delegate sparse defaults and hidden axes to Core"
