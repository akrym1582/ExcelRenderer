import hashlib,json,pathlib,statistics,subprocess
root=pathlib.Path('/workspace/ExcelRenderer')
prior=json.load(open('/tmp/mapping-review-performance-latest.json'))
binaries={'baseline':pathlib.Path('/tmp/excelrenderer-mapping-baseline/tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll'),'current':root/'tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll'}
report={'reason':'Resolve noisy ordinary-Mapping timings with interleaved fresh processes','warmups':1,'samples':5,'implementation':prior['currentHead'],'baseline':prior['baselineCommit'],'cases':[]}
for rows in [10000,100000]:
 path=pathlib.Path('/tmp/mapping-review-benchmark-final/plain-1-template.xlsx')
 case={'rows':rows,'inputSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'runs':{'baseline':[],'current':[]}}
 assert case['inputSha256']==next(c for c in prior['cases'] if c['kind']=='plain' and c['rows']==rows)['inputSha256']
 for iteration in range(6):
  for label in (['baseline','current'] if iteration%2==0 else ['current','baseline']):
   worker=subprocess.run(['dotnet',str(binaries[label]),'integration-worker',str(path),'mapping','openxml',str(rows)],cwd=root,text=True,capture_output=True,check=True)
   sample=json.loads(worker.stdout.strip().splitlines()[-1]);sample['warmup']=iteration==0;sample['order']=iteration
   case['runs'][label].append(sample)
   print(rows,label,iteration,sample['ElapsedMs'],flush=True)
 case['medians']={label:{key:statistics.median(item[key] for item in samples[1:]) for key in ['ElapsedMs','AllocatedBytes','PeakRssBytes']} for label,samples in case['runs'].items()}
 report['cases'].append(case)
 pathlib.Path('/tmp/mapping-review-plain-paired.json').write_text(json.dumps(report,indent=2)+'\n')
