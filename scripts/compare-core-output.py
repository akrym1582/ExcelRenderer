#!/usr/bin/env python3
"""Needs PyMuPDF, pypdf, Pillow, pdftoppm and performance-generated features.xlsx."""
import pathlib,subprocess,json,fitz,re,shutil
from PIL import Image,ImageChops,ImageStat
from pypdf import PdfReader
from pypdf.generic import ContentStream
import argparse
parser=argparse.ArgumentParser(description="Compare PDF structure/raster and native output bytes against a built baseline.")
parser.add_argument("--baseline-worktree", required=True, type=pathlib.Path)
args=parser.parse_args()
B=args.baseline_worktree.resolve()
R=pathlib.Path(__file__).resolve().parents[1];O=R/'TestResults/CoreRefactor/FinalVisual';O.mkdir(parents=True,exist_ok=True)
old=B/'src/ExcelRenderer.Tool/bin/Release/net10.0/ExcelRenderer.Tool.dll';new=R/'src/ExcelRenderer.Tool/bin/Release/net10.0/ExcelRenderer.Tool.dll';font=R/'third_party/NotoSansJP/NotoSansJP-Regular.ttf'
report={'baseline':'028b6214495eb6f6a30a29990d5b6f86f025c502','renderer':'pdftoppm 26.05.0, 96dpi','cases':[]}
def run(*args):return subprocess.check_output([str(x) for x in args],cwd=R,stderr=subprocess.STDOUT,text=True)
def normal(v):
 if isinstance(v,float):return round(v,4)
 if isinstance(v,(str,int,type(None),bool)):return v
 if isinstance(v,dict):return {k:normal(x) for k,x in v.items() if k not in ['xref','id','seqno']}
 try:return [normal(x) for x in v]
 except TypeError:return str(v)
def structure(path):
 result=[];pdf=PdfReader(path)
 with fitz.open(path) as doc:
  for index,page in enumerate(doc):
   text=page.get_text('rawdict'); images=page.get_image_info(); links=page.get_links(); drawings=page.get_drawings()
   geometry=[]
   for operands,op in ContentStream(pdf.pages[index].get_contents(),pdf).operations:
    if op in [b'm',b'l',b'c',b're',b'cm',b'W',b'W*',b'q',b'Q',b'J',b'j',b'w',b'd',b'S',b's',b'f',b'f*',b'B',b'b',b'B*',b'rg',b'RG']:
     geometry.append([op.decode(),normal(operands)])
   result.append(normal({'size':page.rect,'text':text,'images':images,'links':links,'drawings':drawings,'geometry_operators':geometry}))
 return result
cases=[(source.stem,source,[]) for source in sorted((R/'tests/ExcelRenderer.Tests/SampleInputs').glob('*.xlsx'))]
feature=R/'TestResults/CoreRefactor/FinalPerformance/features.xlsx'
cases += [('full-shapes-links',feature,[]),('full-range',feature,['--sheet','Synthetic','--range','B2:F60']),('full-trim-selection',feature,['--sheet','Synthetic','--range','B2:F60','--trim','--pages','2,1'])]
for name,source,options in cases:
 for tag,tool in [('old',old),('new',new)]:
  pdf=O/(name+'-'+tag+'.pdf');pdf.unlink(missing_ok=True)
  run('dotnet',tool,'render',source,'--format','pdf','-o',pdf,'--font-file',font,'--no-system-fonts',*options)
  run('pdftoppm','-r',96,'-png',pdf,O/(name+'-'+tag))
 a=structure(O/(name+'-old.pdf'));b=structure(O/(name+'-new.pdf'))
 (O/(name+'-old.json')).write_text(json.dumps(a));(O/(name+'-new.json')).write_text(json.dumps(b));assert a==b, name+' structure differs'
 olds=sorted(O.glob(name+'-old-*.png'));news=sorted(O.glob(name+'-new-*.png'));assert len(olds)==len(news)
 mae=0
 for x,y in zip(olds,news):
  with Image.open(x) as p,Image.open(y) as q:
   assert p.size==q.size;mae=max(mae,sum(ImageStat.Stat(ImageChops.difference(p.convert('RGB'),q.convert('RGB'))).mean)/3)
 assert mae==0,(name,mae)
 report['cases'].append({'case':name,'pages':len(olds),'max_pixel_mae':mae,'structure_equal':True})
 print(name,len(olds),mae,'structure equal',flush=True)
# Compare native formats and continuous output on a bounded feature selection.
for fmt,layout in [('png','paginated'),('svg','paginated'),('png','continuous'),('svg','continuous'),('markdown','paginated')]:
 name=fmt+'-'+layout
 dirs=[]
 for tag,tool in [('old',old),('new',new)]:
  dest=O/(name+'-'+tag);shutil.rmtree(dest,ignore_errors=True);dirs.append(dest)
  options=['--sheet','Synthetic']
  if fmt != 'markdown':options+=['--range','A1:F20']
  if fmt in ['png','svg']:options+=['--image-layout',layout]
  run('dotnet',tool,'render',feature,'--format',fmt,'-o',dest,'--font-file',font,'--no-system-fonts',*options)
 files=[sorted(p.relative_to(d) for p in d.rglob('*') if p.is_file()) for d in dirs];assert files[0]==files[1],name
 for path in files[0]:
  a=(dirs[0]/path).read_bytes();b=(dirs[1]/path).read_bytes();assert a==b,(name,path)
 report['cases'].append({'case':name,'files':len(files[0]),'bytes_equal':True});print(name,'bytes equal',flush=True)

# The independently usable Core product is compared against the historical Slim engine.
coreout=O/'core';coreout.mkdir(parents=True,exist_ok=True)
core_old=B/'tools/ExcelRenderer.Slim.Validation/bin/Release/net10.0/ExcelRenderer.Slim.Validation.dll'
core_new=R/'tools/ExcelRenderer.Core.Validation/bin/Release/net10.0/ExcelRenderer.Core.Validation.dll'
report['core_comparison']=[]
for source in sorted((R/'tests/ExcelRenderer.Tests/SampleInputs').glob('*.xlsx')):
 normalized=coreout/source.name;run('dotnet',core_new,'normalize',source,normalized)
 for tag,tool in [('old',core_old),('new',core_new)]:
  pdf=coreout/(source.stem+'-'+tag+'.pdf');run('dotnet',tool,'render',normalized,pdf,font,1)
  for previous in coreout.glob(source.stem+'-'+tag+'-*.png'):previous.unlink()
  run('pdftoppm','-r',96,'-png',pdf,coreout/(source.stem+'-'+tag))
 assert structure(coreout/(source.stem+'-old.pdf'))==structure(coreout/(source.stem+'-new.pdf')),source.name
 olds=sorted(coreout.glob(source.stem+'-old-*.png'));news=sorted(coreout.glob(source.stem+'-new-*.png'));assert len(olds)==len(news)
 for old_image,new_image in zip(olds,news):
  with Image.open(old_image) as a,Image.open(new_image) as b:
   assert a.size==b.size and ImageChops.difference(a.convert('RGB'),b.convert('RGB')).getbbox() is None,source.name
 report['core_comparison'].append({'fixture':source.name,'pages':len(olds),'max_pixel_mae':0,'structure_equal':True})
 print(source.name,'Core structure/raster equal',flush=True)

(R/'docs/core-refactor-visual-results.json').write_text(json.dumps(report,indent=2)+'\n')
