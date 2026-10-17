#!/usr/bin/env python3
"""Summarize fixed warmup + five-repeat expansion benchmarks without hiding failures."""
import argparse
from collections import defaultdict
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import statistics

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('directory', type=Path)
parser.add_argument('--baseline', required=True)
parser.add_argument('--revision', required=True)
a = parser.parse_args()
series = []
for path in sorted(a.directory.glob('*.jsonl')):
    groups = defaultdict(list)
    for line in path.read_text(encoding='utf-8').splitlines():
        row = json.loads(line)
        if 'Name' in row:
            groups[(row['Name'], row['Count'])].append(row)
    for (name, count), rows in groups.items():
        assert len(rows) == 6 and sum(r['Warmup'] for r in rows) == 1, (path, name, count)
        measured = [r for r in rows if not r['Warmup']]
        assert len(measured) == 5
        success = measured[0].get('Success', True)
        assert all(r.get('Success', True) == success for r in measured)
        if path.name.startswith('current-'):
            assert success, (path, name, count)
        checksum = measured[0].get('Checksum', measured[0].get('Result'))
        assert all(r.get('Checksum', r.get('Result')) == checksum for r in measured)
        series.append(dict(file=path.name, name=name, count=count, success=success,
                           diagnostic=measured[0].get('Diagnostic'), checksum=checksum,
                           medianMilliseconds=statistics.median(r['Milliseconds'] for r in measured),
                           medianAllocatedBytes=statistics.median(r['AllocatedBytes'] for r in measured),
                           gc=[statistics.median(r[g] for r in measured) for g in ('Gen0', 'Gen1', 'Gen2')] if all(g in measured[0] for g in ('Gen0', 'Gen1', 'Gen2')) else None,
                           statistics=measured[0].get('Statistics')))
root = Path(__file__).resolve().parents[2]
harness = Path(__file__).resolve().parent
sources = ['ExpansionBenchmark.cs', 'Program.cs', 'Benchmark.cs', 'EngineBenchmark.cs', 'WorkbookFixture.cs', 'ExcelRenderer.Formulas.Performance.csproj']
summary = dict(baselineRevision=a.baseline, implementationRevision=a.revision,
               recordedAt=datetime.now(timezone.utc).isoformat(), warmups=1, measuredRepeats=5,
               harnessSha256={name: hashlib.sha256((harness/name).read_bytes()).hexdigest() for name in sources},
               rawSha256={p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(a.directory.glob('*.jsonl'))},
               notes=['Baseline uses the identical harness with EXPANSION_BASELINE only to omit statistics unavailable in that API.',
                      'Baseline unsupported/limit failures are retained; they are not speed comparisons.',
                      'Allocated bytes are total managed allocation, not retained memory or peak RSS.',
                      'Exact lookup indexes are session-local and bounded; varied keys are measured separately.'],
               series=series)
(a.directory/'performance-summary.json').write_text(json.dumps(summary, indent=2)+'\n', encoding='utf-8')
print(f'Summarized {len(series)} workload/size pairs.')
