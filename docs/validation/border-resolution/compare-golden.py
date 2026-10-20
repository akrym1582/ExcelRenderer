"""Compare retained parent outputs and update only reviewed visual hashes."""
from pathlib import Path
import hashlib
import json
import math
import shutil

import numpy as np
from PIL import Image, ImageDraw

root = Path.cwd()
evidence = root / "docs/validation/border-resolution"
work = root / "artifacts/border-resolution"
original = json.loads((root / "tests/ExcelRenderer.Tests/ReaderGolden/artifacts.json").read_text())
before = json.loads((work / "golden-before/hashes.json").read_text())
after = json.loads((work / "golden-after/hashes.json").read_text())
updates = {}
changes = []
pixels = []
for path, outputs in after.items():
    assert outputs.keys() == original[path].keys(), path
    masks = json.loads((work / "golden-after" / path / "border-masks.json").read_text())
    for key, digest in outputs.items():
        assert before[path][key] == original[path][key], (path, key, "parent baseline differs")
        if digest == original[path][key]:
            continue
        assert not key.startswith("Markdown/"), (path, key)
        updates.setdefault(path, {})[key] = digest
        changes.append([path, key])
        if not key.startswith("Png/"):
            continue
        old = Image.open(work / "golden-before" / path / key).convert("RGB")
        new = Image.open(work / "golden-after" / path / key).convert("RGB")
        assert old.size == new.size, (path, key)
        delta = np.any(np.asarray(old) != np.asarray(new), axis=2)
        mask = Image.new("L", new.size)
        draw = ImageDraw.Draw(mask)
        for x1, y1, x2, y2 in masks[Path(key).name]:
            draw.rectangle((math.floor(x1 * 96 / 72), math.floor(y1 * 96 / 72), math.ceil(x2 * 96 / 72), math.ceil(y2 * 96 / 72)), fill=255)
        outside = int(np.count_nonzero(delta & (np.asarray(mask) == 0)))
        assert outside == 0, (path, key, outside)
        points = np.argwhere(delta)
        bounds = [int(points[:, 1].min()), int(points[:, 0].min()), int(points[:, 1].max()), int(points[:, 0].max())] if len(points) else None
        stem = hashlib.sha256((path + "/" + key).encode()).hexdigest()[:12]
        target = evidence / "golden-pixel-diffs"
        target.mkdir(exist_ok=True)
        display = Image.new("RGB", new.size, "white")
        image = np.asarray(display).copy()
        image[delta] = [255, 0, 0]
        Image.fromarray(image).save(target / (stem + ".png"))
        if Path(path).name in ("05-borders.xlsx", "09-cell-border.xlsx"):
            shutil.copyfile(work / "golden-before" / path / key, target / (stem + "-before.png"))
            shutil.copyfile(work / "golden-after" / path / key, target / (stem + "-after.png"))
        pixels.append({"input": path, "artifact": key, "changedPixels": int(np.count_nonzero(delta)), "outsideBorderMask": outside, "bounds": bounds, "diff": stem + ".png"})

(root / "tests/ExcelRenderer.Tests/ReaderGolden/border-artifacts.json").write_text(json.dumps(updates, indent=2, ensure_ascii=False) + "\n")
(evidence / "golden-differences.json").write_text(json.dumps({"baselineMismatch": [], "changed": changes, "pngPixelChecks": pixels}, indent=2, ensure_ascii=False) + "\n")
print(f"{len(changes)} changed visual artifacts; {len(pixels)} PNGs checked; no changed pixels outside border masks; Markdown unchanged")
