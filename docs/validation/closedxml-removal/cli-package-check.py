from pathlib import Path
import subprocess,json,os,hashlib,zipfile
root=Path('/workspace/ExcelRenderer');out=Path('/tmp/excelrenderer-removal-cli-final');out.mkdir(exist_ok=True)
exe='/tmp/excelrenderer-removal-tool-accepted-v2/excelrenderer'
env=dict(os.environ,DOTNET_ROOT='/workspace/.tools/dotnet')
template=root/'samples/mapping/invoice/template.xlsx';data=template.parent/'data.json';font=root/'third_party/NotoSansJP/NotoSansJP-Regular.ttf'
commands=[['xlsx',str(template),'--data',str(data),'-o',str(out/'mapped.xlsx'),'--mapping-backend','auto','--verbose']]
for fmt in ['pdf','png','svg','markdown']:
 target=out/('render-'+fmt if fmt in ['png','svg'] else 'render.'+fmt)
 commands.append(['render',str(template),'--data',str(data),'--format',fmt,'-o',str(target),'--reader-backend','auto','--mapping-backend','auto','--font-file',str(font),'--font-policy','requested','--verbose'])
results=[]
for args in commands:
 run=subprocess.run([exe,*args],cwd=root,env=env,text=True,capture_output=True)
 results.append({'args':args,'exitCode':run.returncode,'stdout':run.stdout,'stderr':run.stderr})
 if run.returncode:raise RuntimeError(results[-1])
for backend in ['reader','mapping']:
 sentinel=out/(backend+'-sentinel');sentinel.write_text('unchanged')
 run=subprocess.run([exe,'pdf','missing.xlsx','--data','missing.json','-o',str(sentinel),'--'+backend+'-backend','closedxml'],cwd=root,env=env,text=True,capture_output=True)
 assert run.returncode and 'ClosedXML has been removed' in run.stderr+run.stdout and sentinel.read_text()=='unchanged'
 results.append({'removedBackend':backend,'exitCode':run.returncode,'stderr':run.stderr,'outputPreserved':True})
artifacts=[{'file':str(p.relative_to(out)),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(out.rglob('*')) if p.is_file() and 'sentinel' not in p.name]
assert any(item['file'].endswith('.pdf') for item in artifacts)
assert any(item['file'].endswith('.svg') for item in artifacts)
assert any(item['file'].endswith('.png') for item in artifacts)
# Prove that the installed executable uses the package that was audited.
package=root/'unused'
with zipfile.ZipFile('/tmp/excelrenderer-removal-packages/ExcelRenderer.Tool.1.8.0.nupkg') as pkg:
 entries=[name for name in pkg.namelist() if name.endswith('/ExcelRenderer.OpenXml.dll')]
 expected=hashlib.sha256(pkg.read(entries[0])).hexdigest()
 installed=list(Path('/tmp/excelrenderer-removal-tool-accepted-v2').rglob('ExcelRenderer.OpenXml.dll'))
 assert installed and all(hashlib.sha256(p.read_bytes()).hexdigest()==expected for p in installed)
Path('/tmp/removal-cli-results.json').write_text(json.dumps({'commands':results,'artifacts':artifacts,'installedOpenXmlSha256':expected,'installedPackageMatches':True},indent=2)+'\n')
print(json.dumps({'commands':len(results),'artifacts':len(artifacts),'installedPackageMatches':True}))
