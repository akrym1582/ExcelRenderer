#!/usr/bin/env python3
"""Build the MIT native subset adapter from checksum-pinned official HarfBuzz source.

Requires CMake >=3.16 and a C++ compiler (MSVC on Windows). No system HarfBuzz,
Python/fontTools, or external subset executable is used by the product runtime.
"""
import argparse
import ctypes
import hashlib
import json
import platform
from pathlib import Path
import shutil
import subprocess
import tarfile
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
VERSION = "10.2.0"
URL = f"https://github.com/harfbuzz/harfbuzz/archive/refs/tags/{VERSION}.tar.gz"
SHA256 = "11749926914fd488e08e744538f19329332487a6243eec39ef3c63efa154a578"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work", type=Path, default=ROOT / "artifacts/harfbuzz-build")
    parser.add_argument("--archive", type=Path)
    parser.add_argument("--cmake", default="cmake")
    parser.add_argument("--jobs", type=int, default=2)
    args = parser.parse_args()
    machine = platform.machine().lower()
    architecture = {"amd64": "x64", "x86_64": "x64", "aarch64": "arm64", "arm64": "arm64"}.get(machine)
    system = {"Linux": "linux", "Windows": "win", "Darwin": "osx"}.get(platform.system())
    if not architecture or not system:
        raise SystemExit(f"No native build recipe for {platform.platform()}")
    rid = f"{system}-{architecture}"
    args.work.mkdir(parents=True, exist_ok=True)
    archive = args.archive or args.work / f"harfbuzz-{VERSION}.tar.gz"
    if not archive.exists():
        with urllib.request.urlopen(URL) as response, archive.open("wb") as output:
            shutil.copyfileobj(response, output)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    if digest != SHA256:
        raise SystemExit(f"HarfBuzz archive SHA-256 mismatch: {digest}")
    source = args.work / f"harfbuzz-{VERSION}"
    if not source.exists():
        with tarfile.open(archive) as package:
            package.extractall(args.work, filter="data")
    else:
        # A valid cached archive does not establish that its extracted tree is
        # unchanged. Refuse modified source rather than attesting to a different build.
        with tarfile.open(archive) as package:
            for member in package.getmembers():
                if member.isfile():
                    cached = args.work / member.name
                    original = package.extractfile(member)
                    if not cached.is_file() or hashlib.sha256(cached.read_bytes()).digest() != hashlib.sha256(original.read()).digest():
                        raise SystemExit(f"Cached HarfBuzz source differs from the pinned archive: {member.name}; use a fresh --work directory")
    build = args.work / "build"
    subprocess.run([args.cmake, "-S", str(ROOT / "native/harfbuzz"), "-B", str(build),
                    f"-DHB_SOURCE={source.resolve()}", "-DCMAKE_BUILD_TYPE=Release"], check=True)
    subprocess.run([args.cmake, "--build", str(build), "--config", "Release", "--parallel", str(args.jobs)], check=True)
    filename = {"linux": "libExcelRenderer.HarfBuzzSubset.so", "win": "ExcelRenderer.HarfBuzzSubset.dll",
                "osx": "libExcelRenderer.HarfBuzzSubset.dylib"}[system]
    matches = list(build.rglob(filename))
    if len(matches) != 1:
        raise SystemExit(f"Expected exactly one {filename}, got {matches}")
    output = ROOT / "native/runtimes" / rid / "native"
    output.mkdir(parents=True, exist_ok=True)
    target = output / filename
    shutil.copy2(matches[0], target)
    library = ctypes.CDLL(str(target))
    for symbol in ("er_subset", "er_blob_data", "er_blob_destroy", "er_version"):
        getattr(library, symbol)
    library.er_version.restype = ctypes.c_char_p
    if library.er_version().decode() != VERSION:
        raise SystemExit("Native HarfBuzz version mismatch")
    metadata = {"rid": rid, "version": VERSION, "source": URL, "source_sha256": SHA256,
                "binary_sha256": hashlib.sha256(target.read_bytes()).hexdigest(),
                "license": "Old MIT (HarfBuzz), MIT (adapter)", "exports": ["er_subset", "er_blob_data", "er_blob_destroy", "er_version"],
                "platform": platform.platform()}
    (output.parent / "build.json").write_text(json.dumps(metadata, indent=2) + "\n")
    print(json.dumps(metadata, indent=2))


if __name__ == "__main__":
    main()
