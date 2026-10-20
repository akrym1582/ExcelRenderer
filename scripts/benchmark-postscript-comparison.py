#!/usr/bin/env python3
"""Measure the real PostScript image comparator in a saved Release test runtime.

Reuse one fixture directory for both versions. Build and snapshot the test runtime
before invoking this script; do not run other builds, tests or rendering alongside it.
"""

import argparse
import hashlib
import json
import shutil
import statistics
import subprocess
from pathlib import Path
from xml.sax.saxutils import escape

HARNESS = r'''
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

var runtime = AppContext.BaseDirectory;
var assembly = Assembly.LoadFrom(Path.Combine(runtime, "ExcelRenderer.Tests.dll"));
var compare = assembly.GetType("ExcelRenderer.Tests.PostScriptGhostscriptTests")!
    .GetMethod("Compare", BindingFlags.NonPublic | BindingFlags.Static)!;
var fixtures = args[0];
var output = args[1];
var repetitions = int.Parse(args[2]);
Directory.CreateDirectory(output);
using (var warmup = new SKBitmap(32, 32))
{
    warmup.Erase(SKColors.Gray);
    for (var i = 0; i < 3; i++)
        compare.Invoke(null, [warmup, warmup, Path.Combine(output, "warmup.png")]);
}
Console.WriteLine(JsonSerializer.Serialize(new {
    kind = "environment", runtime = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription, processors = Environment.ProcessorCount,
    warmups = 3, warmupWidth = 32, warmupHeight = 32,
}));
using var expected = SKBitmap.Decode(Path.Combine(fixtures, "drawing-png.png"));
foreach (var name in new[] { "drawing-ps", "missing-glyph", "displaced", "missing-line" })
{
    using var actual = SKBitmap.Decode(Path.Combine(fixtures, name + ".png"));
    for (var round = 0; round < repetitions; round++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocated = GC.GetTotalAllocatedBytes(true);
        var timer = Stopwatch.StartNew();
        var path = Path.Combine(output, name + "-diff.png");
        var result = compare.Invoke(null, [expected, actual, path])!;
        timer.Stop();
        var allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated;
        using var diff = SKBitmap.Decode(path);
        // Compare decoded colors, independent of PNG encoder metadata/compression.
        var pixelBytes = new byte[diff.Width * diff.Height * 4];
        var pixels = diff.Pixels;
        for (var i = 0; i < pixels.Length; i++)
        {
            pixelBytes[i * 4] = pixels[i].Red;
            pixelBytes[(i * 4) + 1] = pixels[i].Green;
            pixelBytes[(i * 4) + 2] = pixels[i].Blue;
            pixelBytes[(i * 4) + 3] = pixels[i].Alpha;
        }
        Console.WriteLine(JsonSerializer.Serialize(new {
            kind = "sample", name, round, expected.Width, expected.Height,
            elapsedMs = timer.Elapsed.TotalMilliseconds, allocatedBytes,
            result = JsonSerializer.SerializeToElement(result, result.GetType()),
            diffPixelSha256 = Convert.ToHexString(SHA256.HashData(pixelBytes)),
            diffFileSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
        }));
        Console.Out.Flush();
    }
}
'''


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime', required=True, type=Path)
    parser.add_argument('--fixtures', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--repetitions', default=3, type=int)
    parser.add_argument('--reference', type=Path, help='Prior output directory; require identical metrics and diff pixels')
    args = parser.parse_args()
    runtime, fixtures, output = (p.resolve() for p in (args.runtime, args.fixtures, args.output))
    if args.repetitions < 1:
        parser.error('--repetitions must be positive')
    output.mkdir(parents=True, exist_ok=False)
    harness = output / 'harness'
    harness.mkdir()
    (harness / 'Program.cs').write_text(HARNESS)
    (harness / 'ComparisonBenchmark.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
        '<Nullable>enable</Nullable><IsPackable>false</IsPackable></PropertyGroup>'
        '<ItemGroup><Reference Include="SkiaSharp"><HintPath>'
        + escape(str(runtime / 'SkiaSharp.dll'))
        + '</HintPath></Reference></ItemGroup></Project>')
    build = subprocess.run(
        ['dotnet', 'build', '-c', 'Release', '-m:1', '-p:ImportDirectoryBuildProps=false', '-p:NuGetAudit=false'],
        cwd=harness, capture_output=True, text=True)
    (output / 'build.log').write_text(build.stdout + build.stderr)
    build.check_returncode()
    for suffix in ('.dll', '.deps.json', '.runtimeconfig.json'):
        shutil.copy2(harness / 'bin/Release/net10.0' / ('ComparisonBenchmark' + suffix), runtime)
    manifest = {
        'runtime': str(runtime), 'fixtures': str(fixtures), 'repetitions': args.repetitions,
        'dllSha256': {p.name: sha256(p) for p in sorted(runtime.glob('*.dll'))},
        'nativeSha256': {str(p.relative_to(runtime)): sha256(p)
                         for p in sorted((runtime / 'runtimes').rglob('*'))
                         if p.is_file() and p.suffix in ('.so', '.dylib', '.dll')},
        'inputSha256': {p.name: sha256(p) for p in sorted(fixtures.glob('*.png'))},
        'harnessSha256': hashlib.sha256(HARNESS.encode()).hexdigest(),
    }
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    with (output / 'samples.jsonl').open('w') as log, (output / 'stderr.log').open('w') as errors:
        process = subprocess.Popen(
            ['dotnet', 'exec', '--depsfile', str(runtime / 'ExcelRenderer.Tests.deps.json'),
             '--runtimeconfig', str(runtime / 'ExcelRenderer.Tests.runtimeconfig.json'),
             str(runtime / 'ComparisonBenchmark.dll'), str(fixtures), str(output), str(args.repetitions)],
            stdout=subprocess.PIPE, stderr=errors, text=True)
        for line in process.stdout:
            print(line, end='', flush=True)
            log.write(line)
            log.flush()
        if process.wait():
            raise RuntimeError('Comparison benchmark failed; inspect samples.jsonl and stderr.log')
    summarize(output, args.repetitions, args.reference)


def summarize(output, repetitions, reference_directory=None):
    manifest = json.loads((output / 'manifest.json').read_text())
    samples = [json.loads(line) for line in (output / 'samples.jsonl').read_text().splitlines() if line.startswith('{')]
    samples = [sample for sample in samples if sample['kind'] == 'sample']
    names = {'drawing-ps', 'missing-glyph', 'displaced', 'missing-line'}
    assert {sample['name'] for sample in samples} == names, 'Missing comparison cases'
    reference = None
    if reference_directory:
        reference_manifest = json.loads((reference_directory / 'manifest.json').read_text())
        assert manifest['inputSha256'] == reference_manifest['inputSha256'], 'Fixture bytes changed'
        assert manifest['harnessSha256'] == reference_manifest['harnessSha256'], 'Harness changed'
        assert manifest['dllSha256']['SkiaSharp.dll'] == reference_manifest['dllSha256']['SkiaSharp.dll'], 'SkiaSharp changed'
        assert manifest['nativeSha256'] == reference_manifest['nativeSha256'], 'Native dependencies changed'
        reference = [json.loads(line) for line in (reference_directory / 'samples.jsonl').read_text().splitlines() if line.startswith('{')]
        reference = [sample for sample in reference if sample['kind'] == 'sample']
    summary = {}
    for name in sorted({sample['name'] for sample in samples}):
        group = [sample for sample in samples if sample['name'] == name]
        assert len(group) == repetitions, f'Missing samples: {name}'
        for sample in group:
            assert sample['result'] == group[0]['result'], f'Unstable metrics: {name}'
            assert sample['diffPixelSha256'] == group[0]['diffPixelSha256'], f'Unstable diff pixels: {name}'
        timings = [sample['elapsedMs'] for sample in group]
        summary[name] = {'elapsedMs': timings, 'medianMs': statistics.median(timings)}
        if reference is not None:
            baseline = [sample for sample in reference if sample['name'] == name]
            assert baseline, f'Missing reference: {name}'
            for sample in baseline:
                assert sample['result'] == group[0]['result'], f'Metrics changed: {name}'
                assert sample['diffPixelSha256'] == group[0]['diffPixelSha256'], f'Diff pixels changed: {name}'
            median = statistics.median(sample['elapsedMs'] for sample in baseline)
            summary[name].update(referenceMedianMs=median, speedup=median / statistics.median(timings))
    (output / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()
