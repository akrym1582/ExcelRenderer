#!/usr/bin/env python3
"""Prove reviewed AA exceptions cannot hide removed text or extra vector ink."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

import fitz

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, default=ROOT / "artifacts/pdf-comparison")
    args = parser.parse_args()
    for mutation in ("remove-ivs", "extra-ink"):
        with tempfile.TemporaryDirectory(prefix="pdf-comparison-mutation-") as work:
            work = Path(work)
            target = work / "commands-1"
            shutil.copytree(args.baseline / "commands-1", target)
            source = target / "skiasharp.pdf"
            document = fitz.open(source)
            if mutation == "remove-ivs":
                document[0].add_redact_annot(fitz.Rect(19, 52, 32, 67), fill=(1, 1, 1))
                document[0].apply_redactions()
            else:
                document[0].draw_line((20, 52), (50, 67), color=(0, 0, 0), width=.2)
            document.save(target / "mutated.pdf")
            document.close()
            (target / "mutated.pdf").replace(source)
            process = subprocess.run([sys.executable, str(ROOT / "scripts/compare-pdf-backends.py"), "--no-generate", "--fixture", "commands-1", "--output", str(work)], cwd=ROOT)
            report = json.loads((work / "report.json").read_text())
            if process.returncode == 0 or report["requiresFix"] == 0:
                raise SystemExit(f"Mutation escaped comparison: {mutation}")
            print(f"Detected {mutation}: {report['requiresFix']} unmet gates", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
