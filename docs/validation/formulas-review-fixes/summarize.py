from pathlib import Path
import json,statistics
root=Path('docs/validation/formulas-review-fixes')
rows=[]
for mode in ['engine','recalculate','mapping','rounding']:
 before=[json.loads(line) for line in (root/f'before-{mode}.jsonl').read_text().splitlines()]
 after=[json.loads(line) for line in (root/f'after-{mode}.jsonl').read_text().splitlines()]
 assert before[0]==after[0]
 for name,count in sorted({(r['Name'],r['Count']) for r in before if 'Name' in r}):
  b=[r for r in before if r.get('Name')==name and r.get('Count')==count]
  a=[r for r in after if r.get('Name')==name and r.get('Count')==count]
  assert len(a)==len(b)==6
  assert [r['Result'] for r in a]==[r['Result'] for r in b]
  b=[r for r in b if not r['Warmup']];a=[r for r in a if not r['Warmup']]
  assert len(a)==len(b)==5
  bm=statistics.median(r['Milliseconds'] for r in b);am=statistics.median(r['Milliseconds'] for r in a)
  bb=statistics.median(r['AllocatedBytes'] for r in b);ab=statistics.median(r['AllocatedBytes'] for r in a)
  rows.append(dict(case=name,count=count,before_ms=bm,after_ms=am,time_ratio=am/bm,before_allocated_bytes=bb,after_allocated_bytes=ab,allocation_ratio=ab/bb,before_max_peak_bytes=max(r['ProcessPeakWorkingSetBytes'] for r in b),after_max_peak_bytes=max(r['ProcessPeakWorkingSetBytes'] for r in a)))
assert len(rows)==24
(root/'performance-summary.json').write_text(json.dumps(dict(baseline_commit='492f07b03f4a92be1e316d0afb939a0a48fb9711',validation_commit='ec174461a17289dadda7c2d1d15432e3e6231318',warmups=1,repeats=5,tiered_compilation=0,rows=rows),indent=2)+'\n')
md=['| Case | Rows | Before ms | After ms | Time ratio | Before MiB | After MiB |','| --- | ---: | ---: | ---: | ---: | ---: | ---: |']
for r in rows:
 md.append(f"| {r['case']} | {r['count']} | {r['before_ms']:.2f} | {r['after_ms']:.2f} | {r['time_ratio']:.3f} | {r['before_allocated_bytes']/1048576:.2f} | {r['after_allocated_bytes']/1048576:.2f} |")
large={r['case']:r for r in rows if r['count']==30000}
x=large['xlsx-recalculate-save'];m=large['mapping-expand-recalculate-save']
decimals=[r for r in large.values() if 'decimal' in r['case']]
decimal_lo=min(r['time_ratio'] for r in decimals);decimal_hi=max(r['time_ratio'] for r in decimals)
summary=f"""At 30,000 rows, XLSX recalculation/save changed by {(x['time_ratio']-1)*100:+.1f}%
and Mapping/recalculate/save by {(m['time_ratio']-1)*100:+.1f}%. Managed allocation changes were
{(x['allocation_ratio']-1)*100:+.2f}% and {(m['allocation_ratio']-1)*100:+.2f}%, respectively.
Actual decimal rounding costs {decimal_lo:.2f}–{decimal_hi:.2f}x the old binary-scaling implementation
on these 30,000-operation cases, allocating about {decimals[0]['allocation_ratio']:.2f}x as much.
This additional conversion/BigInteger cost is explicit. Integer and decimal
no-change exits are present; correctness is not traded for the old rounding path.
Engine sessions also include typed tokens and propagated source spans; complete
size-by-size figures are below. These local timings do not establish general overhead or speedup.
Both baseline and final captures ran sequentially without concurrent repository
builds, tests or package-consumer work. The final captures include the signed-zero and empty-sheet-name
compatibility changes.

"""
print(summary)
print('\n'.join(md))
