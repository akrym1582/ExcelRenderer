#!/usr/bin/env python3
"""Run each measurement in a fresh process and sample Linux RSS/private memory."""
import argparse
import hashlib
import json
import pathlib
import shutil
import subprocess
import time

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("dll")
parser.add_argument("input")
parser.add_argument("prefix")
parser.add_argument("--runs", type=int, default=3)
parser.add_argument("--repeat", type=int, default=1)
parser.add_argument("--font", default="third_party/NotoSansJP/NotoSansJP-Regular.ttf")
options = parser.parse_args()
prefix = pathlib.Path(options.prefix)
prefix.parent.mkdir(parents=True, exist_ok=True)
# Different stages can operate on the same fixture without sharing an output stream.
source = prefix.with_suffix(".xlsx")
shutil.copyfile(options.input, source)
for run in range(options.runs):
    stdout = pathlib.Path(f"{prefix}-{run}.jsonl")
    stderr = pathlib.Path(f"{prefix}-{run}.stderr")
    maximum_rss = maximum_private = 0
    started = time.monotonic()
    samples = 0
    with stdout.open("w") as out, stderr.open("w") as err:
        process = subprocess.Popen(
            ["dotnet", options.dll, "run", str(source), str(options.repeat), options.font],
            stdout=out, stderr=err)
        while process.poll() is None:
            try:
                status = pathlib.Path(f"/proc/{process.pid}/status").read_text()
                rss = next(int(line.split()[1]) * 1024 for line in status.splitlines() if line.startswith("VmRSS:"))
                maximum_rss = max(maximum_rss, rss)
                rollup = pathlib.Path(f"/proc/{process.pid}/smaps_rollup").read_text()
                private = sum(int(line.split()[1]) * 1024 for line in rollup.splitlines()
                              if line.startswith(("Private_Clean:", "Private_Dirty:")))
                maximum_private = max(maximum_private, private)
                samples += 1
            except (FileNotFoundError, ProcessLookupError, PermissionError, StopIteration):
                pass
            time.sleep(0.01)
    sample_path = pathlib.Path(f"{prefix}-{run}.sample.json")
    sample_path.write_text(json.dumps({
        "ExitCode": process.returncode,
        "ElapsedMs": (time.monotonic() - started) * 1000,
        "InputSHA256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "InputBytes": source.stat().st_size,
        "SampleIntervalMs": 10, "Samples": samples,
        "PeakRssBytes": maximum_rss, "PeakPrivateResidentBytes": maximum_private,
    }) + "\n")
    if process.returncode:
        raise SystemExit(f"Conversion failed ({process.returncode}); see {stderr} and {sample_path}")
    shutil.copyfile(str(source) + ".pdf", f"{prefix}-{run}.pdf")
    result = json.loads(stdout.read_text().splitlines()[0])
    print(f"{prefix.name} run {run}: {result['TotalMs']:.1f} ms, "
          f"peak RSS {maximum_rss / 1048576:.1f} MiB, {result['Pages']} pages", flush=True)
