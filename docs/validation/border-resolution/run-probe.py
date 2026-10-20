"""Build isolated Release probes without adding a product project or package."""
import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile
from xml.sax.saxutils import quoteattr

parser = argparse.ArgumentParser()
parser.add_argument("kind", choices=["benchmark", "golden"])
parser.add_argument("--source-root", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--build-only", action="store_true")
args = parser.parse_args()
root = Path(__file__).resolve().parents[3]
source = args.source_root.resolve()
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)
for filesystem in (output, Path(tempfile.gettempdir())):
    if shutil.disk_usage(filesystem).free < 1024 ** 3:
        raise SystemExit(f"Less than 1 GiB free: {filesystem}")

with tempfile.TemporaryDirectory(prefix="excel-border-probe-") as temporary:
    project = Path(temporary)
    shutil.copyfile(Path(__file__).with_name("BorderBenchmark.cs" if args.kind == "benchmark" else "BorderGolden.cs"), project / "Program.cs")
    shared = f'<Compile Include={quoteattr(str(root / "tests/Shared/ReaderSnapshot.cs"))} Link="ReaderSnapshot.cs"/>' if args.kind == "golden" else ""
    (project / "BorderProbe.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
        '<Nullable>enable</Nullable><IsPackable>false</IsPackable></PropertyGroup><ItemGroup>'
        f'<ProjectReference Include={quoteattr(str(source / "src/ExcelRenderer/ExcelRenderer.csproj"))}/>'
        + shared + '</ItemGroup></Project>'
    )
    subprocess.run(["dotnet", "build", str(project / "BorderProbe.csproj"), "-c", "Release", "-o", str(output / "runtime"), "-m:1", "--verbosity:minimal"], check=True, cwd=root)

command = ["dotnet", str(output / "runtime/BorderProbe.dll")]
if args.build_only:
    print("Runtime: " + str(output / "runtime/BorderProbe.dll"))
elif args.kind == "benchmark":
    with (output / "samples.jsonl").open("w") as stream:
        subprocess.run(command + [str(Path(__file__).with_name("benchmark-input.json"))], check=True, cwd=root, stdout=stream)
else:
    subprocess.run(command + [str(root), str(output / "captures")], check=True, cwd=root)
