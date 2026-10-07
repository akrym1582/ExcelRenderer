#!/usr/bin/env python3
"""Compare all PDF pages at 96 dpi without widening pixel tolerances (PyMuPDF)."""
import argparse
import hashlib
import json
import pathlib

import fitz

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("before", type=pathlib.Path)
parser.add_argument("after", type=pathlib.Path)
options = parser.parse_args()
different = []
with fitz.open(options.before) as before, fitz.open(options.after) as after:
    for index in range(max(len(before), len(after))):
        if index >= min(len(before), len(after)):
            different.append(index + 1)
            continue
        left = before[index].get_pixmap(dpi=96, alpha=False)
        right = after[index].get_pixmap(dpi=96, alpha=False)
        if (left.width, left.height, left.samples) != (right.width, right.height, right.samples):
            different.append(index + 1)
    print(json.dumps({
        "Dpi": 96, "BeforePages": len(before), "AfterPages": len(after),
        "DifferentPages": different,
        "BeforeSha256": hashlib.sha256(options.before.read_bytes()).hexdigest(),
        "AfterSha256": hashlib.sha256(options.after.read_bytes()).hexdigest(),
    }, indent=2))
raise SystemExit(1 if different else 0)
