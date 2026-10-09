#!/usr/bin/env python3
"""Reproduce baseline/final measurements without concurrent builds or tests.
Baseline Core allocation reporting needs the validation-harness observer described
in docs/core-refactor-validation.md; product source remains unchanged.
"""
import pathlib,subprocess,json,statistics,zipfile,xml.etree.ElementTree as E
import argparse
parser=argparse.ArgumentParser(description="Compare three isolated baseline/final trials; build both harnesses first.")
parser.add_argument("--baseline-worktree", required=True, type=pathlib.Path)
args=parser.parse_args()
B=args.baseline_worktree.resolve()
R=pathlib.Path(__file__).resolve().parents[1]; O=R/'TestResults/CoreRefactor/FinalPerformance';O.mkdir(parents=True,exist_ok=True)
oldcore=B/'tools/ExcelRenderer.Slim.Validation/bin/Release/net10.0/ExcelRenderer.Slim.Validation.dll';core=R/'tools/ExcelRenderer.Core.Validation/bin/Release/net10.0/ExcelRenderer.Core.Validation.dll'
oldmain=B/'tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll';main=R/'tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll';font=R/'third_party/NotoSansJP/NotoSansJP-Regular.ttf'
def run(*args):return subprocess.check_output([str(x) for x in args],cwd=R,stderr=subprocess.STDOUT,text=True)
def measure(tool,source,product,tag):
 if product=='Core':return json.loads(run('dotnet',tool,'render',source,O/(source.stem+'-'+tag+'.pdf'),font,1).splitlines()[-1])[0]
 return json.loads(run('dotnet',tool,'render-case',source,'Pdf','Paginated',1).splitlines()[-1])
report={'baseline':'028b6214495eb6f6a30a29990d5b6f86f025c502','environment':'Debian 13; SDK 10.0.401; fresh processes, sequential alternating baseline/final; no builds/tests overlapped','cases':[]}
cases=[]
for product,gen in [('Core',core),('Full',main)]:
 for rows in [1000,5000,10000]:
  path=O/f'{product}-{rows}.xlsx';run('dotnet',gen,'generate',path,rows);cases.append((product,str(rows)+' rows',path))
path=O/'repeated-images.xlsx';run('dotnet',core,'generate',path,1000,'images');cases.extend([('Core','100 repeated images',path),('Full','100 repeated images',path)])
path=O/'features.xlsx';run('dotnet',main,'generate',path,200,'mixed')
ns='http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing';a='http://schemas.openxmlformats.org/drawingml/2006/main'
with zipfile.ZipFile(path) as z:files={name:z.read(name) for name in z.namelist()}
drawing=next(name for name in files if name.startswith('xl/drawings/drawing') and name.endswith('.xml'))
root=E.fromstring(files[drawing])
for n in range(20):
 node=E.fromstring(f'''<xdr:oneCellAnchor xmlns:xdr="{ns}" xmlns:a="{a}"><xdr:from><xdr:col>{n%5}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{n*8}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:ext cx="508000" cy="254000"/><xdr:sp><xdr:nvSpPr><xdr:cNvPr id="{100+n}" name="benchmark-{n}"/><xdr:cNvSpPr/></xdr:nvSpPr><xdr:spPr><a:xfrm rot="{n*60000}"><a:off x="0" y="0"/><a:ext cx="508000" cy="254000"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val="DDEEFF"/></a:solidFill><a:ln w="12700"><a:solidFill><a:srgbClr val="112233"/></a:solidFill></a:ln></xdr:spPr></xdr:sp><xdr:clientData/></xdr:oneCellAnchor>''');root.append(node)
files[drawing]=E.tostring(root,encoding='utf-8',xml_declaration=True)
with zipfile.ZipFile(path,'w',compression=zipfile.ZIP_DEFLATED) as z:
 for name,data in files.items():z.writestr(name,data)
cases.append(('Full','shapes, transformed objects and links',path))
cases.append(('Full','font decorations and runs',R/'tests/ExcelRenderer.Tests/SampleInputs/04-text-decoration.xlsx'))
for product,name,path in cases:
 if any(c['product']==product and c['fixture']==name for c in report['cases']):continue
 tools={'old':oldcore if product=='Core' else oldmain,'new':core if product=='Core' else main}
 samples={'old':[],'new':[]}
 for repetition in range(3):
  for tag,tool in tools.items():samples[tag].append(measure(tool,path,product,tag))
  print(product,name,'repeat',repetition+1,flush=True)
 entry={'product':product,'fixture':name,'path':str(path.relative_to(R)),'samples':samples,'comparisons':{}}
 keys=['Milliseconds','PeakRss','ManagedMax','AllocatedBytes'] if product=='Core' else ['TotalMs','PeakWorkingSet64','PeakManagedSampleBytes','AllocatedBytes']
 for key in keys:
  b=statistics.median(x[key] for x in samples['old']);f=statistics.median(x[key] for x in samples['new']);entry['comparisons'][key]={'baseline':b,'final':f,'ratio':f/b if b else None}
 assert samples['old'][0]['Pages']==samples['new'][0]['Pages']
 if product=='Full':
  for s in samples['new']:
   assert s['Metrics']['pagePayloadMax']==1,s['Metrics'];assert s['Metrics']['pdfSave']==1;assert s['Metrics'].get('pdfImport',0)==0
 report['cases'].append(entry);(R/'docs/core-refactor-measurements.json').write_text(json.dumps(report,indent=2)+'\n')
 print(product,name,entry['comparisons'],flush=True)
print('DONE',flush=True)
