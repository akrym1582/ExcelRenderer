#!/usr/bin/env python3
"""Run an isolated, optionally instrumented Mapping worker and retain allocation phases."""
import argparse
import hashlib
import json
import pathlib
import statistics
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--worker', required=True, type=pathlib.Path)
parser.add_argument('--input', required=True, type=pathlib.Path)
parser.add_argument('--output', required=True, type=pathlib.Path)
parser.add_argument('--capture', type=pathlib.Path, help='Save the final sample workbook after the measured operation.')
parser.add_argument('--backend', default='openxml', choices=['closedxml', 'openxml', 'auto'])
parser.add_argument('--rows', type=int, default=100000)
parser.add_argument('--variant', required=True)
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[1]
worker = args.worker.resolve()
source = args.input.resolve()
args.output.parent.mkdir(parents=True, exist_ok=True)
if args.capture:
    args.capture.parent.mkdir(parents=True, exist_ok=True)
samples = []
for index in range(6):
    command = ['dotnet', str(worker), 'integration-worker', str(source), 'mapping', args.backend, str(args.rows)]
    if index == 5 and args.capture:
        command.append(str(args.capture.resolve()))
    result = subprocess.run(command, cwd=root, capture_output=True, text=True, check=True)
    sample = json.loads(result.stdout.strip().splitlines()[-1])
    sample['warmup'] = index == 0
    samples.append(sample)
    print(f'{args.variant} {index}/5: {sample["AllocatedBytes"] / 1048576:.2f} MiB', flush=True)
if args.capture and not args.capture.is_file():
    parser.error('The worker did not save the capture; rebuild a worker supporting the capture argument.')
measured = samples[1:]
summary = {key: statistics.median(sample[key] for sample in measured) for key in ['AllocatedBytes', 'ElapsedMs', 'PeakRssBytes']}
keys = sorted({key for sample in samples for key in sample['IoMetrics'] if key.startswith('allocation.')})
phases = {key: statistics.median(sample['IoMetrics'].get(key, 0) for sample in measured) for key in keys}
report = {
    'variant': args.variant, 'rows': args.rows, 'backend': args.backend,
    'input': str(source), 'inputSha256': hashlib.sha256(source.read_bytes()).hexdigest(),
    'worker': str(worker), 'workerSha256': hashlib.sha256(worker.read_bytes()).hexdigest(),
    'allocationCounter': 'GC.GetTotalAllocatedBytes(true); phases use GC.GetAllocatedBytesForCurrentThread',
    'warmups': 1, 'samples': 5, 'median': summary, 'phaseMedianBytes': phases, 'raw': samples,
}
args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
print(json.dumps({'median': summary, 'phaseMiB': {key: value / 1048576 for key, value in phases.items()}}, indent=2))
