from pathlib import Path
import subprocess,json,hashlib,statistics
root=Path('/workspace/ExcelRenderer')
fixture=Path('/tmp/excelrenderer-removal-perf.xlsx')
cases=[('parent-default','/tmp/excelrenderer-baseline-performance/ExcelRenderer.Performance.dll','closedxml'),('parent-openxml','/tmp/excelrenderer-baseline-performance/ExcelRenderer.Performance.dll','openxml'),('reader-removal','tools/ExcelRenderer.Performance/bin/Debug/net10.0/ExcelRenderer.Performance.dll','openxml')]
results=[]
for label,tool,backend in cases:
 samples=[]
 for run in range(4):
  result=subprocess.run(['dotnet',tool,'integration-worker',str(fixture),'reader',backend,'1000'],cwd=root,text=True,capture_output=True,check=True)
  record=json.loads(result.stdout)
  if run: samples.append(record)
 fields=['ElapsedMs','AllocatedBytes','PeakRssBytes']
 results.append({'case':label,'warmup':1,'measurements':3,'medians':{key:statistics.median(s[key] for s in samples) for key in fields},'samples':samples})
 print(label,results[-1]['medians'],flush=True)
assert len({(r['samples'][0]['Cells'],r['samples'][0]['Checksum']) for r in results})==1
assert all('closedXml.ms' not in s['Metrics'] and 'closedXml' not in s['Metrics'] for s in results[-1]['samples'])
evidence={'baselineCommit':'24c9dea4d67190951d152eef56f76abc943ef733','scope':'Reader after full dependency removal; Debug build, fresh processes, 1000-row Latin fixture, one warmup + three measured samples. Not a general performance guarantee.','fixtureSha256':hashlib.sha256(fixture.read_bytes()).hexdigest(),'fontSha256':hashlib.sha256((root/'third_party/NotoSansJP/NotoSansJP-Regular.ttf').read_bytes()).hexdigest(),'culture':'en-US','results':results}
(root/'docs/validation/closedxml-removal/reader-performance.json').write_text(json.dumps(evidence,indent=2)+'\n')
