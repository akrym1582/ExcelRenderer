from pathlib import Path
import subprocess,json,hashlib,statistics,zipfile,xml.etree.ElementTree as ET
root=Path('/workspace/ExcelRenderer');dotnet='/workspace/.tools/dotnet/dotnet';directory=Path('/tmp/excelrenderer-removal-integration');directory.mkdir(exist_ok=True)
parent='/tmp/excelrenderer-baseline-performance/ExcelRenderer.Performance.dll';current=str(root/'tools/ExcelRenderer.Performance/bin/Debug/net10.0/ExcelRenderer.Performance.dll')
subprocess.run([dotnet,parent,'integration-generate',str(directory),'1000'],cwd=root,check=True,capture_output=True)
subprocess.run([dotnet,parent,'integration-generate',str(directory),'100'],cwd=root,check=True,capture_output=True)
ns={'s':'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
def projection(path):
 with zipfile.ZipFile(path) as package:
  shared=[]
  if 'xl/sharedStrings.xml' in package.namelist():shared=[''.join(item.itertext()) for item in ET.fromstring(package.read('xl/sharedStrings.xml'))]
  r=ET.fromstring(package.read('xl/worksheets/sheet1.xml'));cells=[]
  for cell in r.findall('.//s:c',ns):
   v=cell.find('s:v',ns);inline=cell.find('s:is',ns)
   if v is None and inline is None:continue
   kind=cell.get('t','n');value=v.text or '' if v is not None else ''.join(inline.itertext())
   if kind=='s':kind='str';value=shared[int(value)]
   elif kind=='inlineStr':kind='str'
   cells.append((cell.get('r'),kind,value))
  merges=sorted(m.get('ref') for m in r.findall('.//s:mergeCell',ns))
  return hashlib.sha256(json.dumps([cells,merges],ensure_ascii=False).encode()).hexdigest()
results=[]
for operation,kind,count in [('mapping','plain',1000),('mapping','nested',1000),('mapping','geometry',1000),('render','geometry',100),('mapping-render','geometry',100)]:
 hashes=[]
 for label,tool,backend in [('parent-default',parent,'closedxml'),('parent-openxml',parent,'openxml'),('removal',current,'openxml')]:
  path=directory/f'{kind}-{count}-{"values" if operation=="render" else "template"}.xlsx'
  samples=[]
  for run in range(4):
   mapped=directory/f'{kind}-{count}-{label}-mapped.xlsx'
   args=[dotnet,tool,'integration-worker',str(path),operation,backend,str(count)]
   if operation=='mapping':args.append(str(mapped))
   record=json.loads(subprocess.run(args,cwd=root,text=True,capture_output=True,check=True).stdout)
   if run:samples.append(record)
  if operation=='mapping':hashes.append(projection(mapped))
  result={'operation':operation,'fixture':path.name,'inputSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'case':label,'warmup':1,'measurements':3,'medians':{field:statistics.median(sample[field] for sample in samples) for field in ['ElapsedMs','AllocatedBytes','PeakRssBytes']},'samples':samples}
  results.append(result);print(operation,kind,label,result['medians'],flush=True)
 if hashes:assert len(set(hashes))==1,(kind,hashes)
report={'baselineCommit':'24c9dea4d67190951d152eef56f76abc943ef733','scope':'Debug builds, isolated processes, en-US, one warmup and three measurements. Fixture generation excluded. Peak RSS includes runtime startup. No general speed guarantee.','fontSha256':hashlib.sha256((root/'third_party/NotoSansJP/NotoSansJP-Regular.ttf').read_bytes()).hexdigest(),'mappingProjectionsMatch':True,'results':results}
(root/'docs/validation/closedxml-removal/mapping-render-performance.json').write_text(json.dumps(report,indent=2)+'\n')
