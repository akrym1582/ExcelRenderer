#!/usr/bin/env python3
"""Summarize the recorded JSON lines without hiding warmups or failed checks."""
import json
import pathlib
import statistics

root = pathlib.Path(__file__).parent / "results"
print("| Run | Case | Count | Median ms | Median allocated MiB | Max process peak MiB |")
print("|---|---|---:|---:|---:|---:|")
for path in sorted(root.glob("*.jsonl")):
    groups = {}
    for line in path.read_text().splitlines():
        record = json.loads(line)
        if "Name" in record and not record["Warmup"]:
            groups.setdefault((record["Name"], record["Count"]), []).append(record)
    for (name, count), values in groups.items():
        if len(values) != 5:
            raise ValueError(f"Incomplete samples in {path}: {name}, {count}")
        elapsed = statistics.median(v["Milliseconds"] for v in values)
        allocated = statistics.median(v["AllocatedBytes"] for v in values) / 2**20
        peak = max(v["ProcessPeakWorkingSetBytes"] for v in values) / 2**20
        print(f"| {path.stem} | {name} | {count} | {elapsed:.2f} | {allocated:.2f} | {peak:.2f} |")
