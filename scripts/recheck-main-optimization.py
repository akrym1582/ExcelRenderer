#!/usr/bin/env python3
"""Investigate every primary time/RSS flag under a separate tiering-disabled protocol."""
import argparse
import json
import os
from pathlib import Path
import statistics
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def analyze(rows):
    paired = []
    for name in dict.fromkeys(row['name'] for row in rows):
        for round_index in sorted({row['round'] for row in rows}):
            stages = {stage: [row for row in rows if row['name'] == name and row['round'] == round_index and row['stage'] == stage and not row['warmup']] for stage in ('before', 'after')}
            if not stages['before'] or not stages['after']:
                continue
            medians = {stage: {key: statistics.median(row[key] for row in values) for key in ('milliseconds', 'allocationBytes', 'peakRssBytes')} for stage, values in stages.items()}
            ratios = {key: medians['after'][key] / medians['before'][key] for key in medians['before']}
            peaks = {stage: max(row['peakRssBytes'] for row in rows if row['name'] == name and row['round'] == round_index and row['stage'] == stage) for stage in stages}
            ratios['processPeakRssBytes'] = peaks['after'] / peaks['before']
            paired.append(dict(case=name, round=round_index, medians=medians, processPeaks=peaks, ratios=ratios,
                               investigate=any(ratios[key] > 1.05 for key in ('milliseconds', 'peakRssBytes', 'processPeakRssBytes'))))
    return paired


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--rounds', type=int, default=3)
    args = parser.parse_args()
    work = args.directory.resolve()
    primary = analyze(json.loads((work / 'raw.json').read_text()))
    flags = sorted({row['case'] for row in primary if row['investigate']})
    records = []
    environment = os.environ | {'DOTNET_TieredCompilation': '0'}
    output = work / 'controlled-samples.jsonl'
    output.write_text('')
    for round_index in range(args.rounds):
        for case in flags:
            for stage in ('before', 'after') if round_index % 2 == 0 else ('after', 'before'):
                command = ['dotnet', str(work / stage / 'ExcelRenderer.Performance.dll'), 'render-profile', str(work / 'cases.json'), case, '5', '8']
                result = subprocess.check_output(command, cwd=ROOT, env=environment, text=True)
                samples = [json.loads(line) for line in result.splitlines()]
                record = dict(round=round_index, stage=stage, case=case, command=command, tiering=False, warmups=5, measuredWarmSamples=8, records=samples)
                with output.open('a') as stream:
                    stream.write(json.dumps(record) + '\n')
                records.extend(dict(round=round_index, stage=stage, **sample) for sample in samples[1:])
                print(round_index, case, stage, flush=True)
    (work / 'regression-analysis.json').write_text(json.dumps(dict(
        primary=primary, flaggedCases=flags, controlled=analyze(records),
        interpretation='Every primary case/round over 1.05 is listed. Controlled runs change tiering and warm count; they investigate but do not replace the primary protocol. No pooled-median acceptance.'), indent=2) + '\n')


if __name__ == '__main__':
    main()
