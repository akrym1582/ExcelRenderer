#!/usr/bin/env python3
"""Compare public/protected and nullable contracts against a historical main worktree."""
import argparse
import difflib
import json
import pathlib
import subprocess
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
SNAPSHOT_SOURCE = r"""using System.Reflection;
using System.Runtime.Loader;
var dir = Path.GetFullPath(args[1]);
AssemblyLoadContext.Default.Resolving += (context, name) => {
 var file = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, name.Name + ".dll");
 if (!File.Exists(file)) file = Path.Combine(dir, name.Name + ".dll");
 return File.Exists(file) ? context.LoadFromAssemblyPath(file) : null;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
var lines = new List<string>();
var nullable = new NullabilityInfoContext();
string NullInfo(NullabilityInfo info) => $"{info.ReadState}/{info.WriteState}[{string.Join(",", info.GenericTypeArguments.Select(NullInfo))}]" + (info.ElementType is null ? "" : NullInfo(info.ElementType));
string Attributes(IEnumerable<CustomAttributeData> attributes) => string.Join(";", attributes.Where(a => a.AttributeType.Namespace == "System.Diagnostics.CodeAnalysis" || a.AttributeType == typeof(ObsoleteAttribute) || a.AttributeType == typeof(ParamArrayAttribute)).Select(a => a.ToString()).Order());
foreach (var type in assembly.GetExportedTypes()) {
 lines.Add($"T {type.FullName} {type.Attributes} base={type.BaseType} interfaces={string.Join(",", type.GetInterfaces().Select(x => x.ToString()).Order())}");
 foreach (var member in type.GetMembers(flags)) {
  bool visible = member switch { MethodBase m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly, FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly, PropertyInfo p => p.GetAccessors(true).Any(m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly), EventInfo e => e.AddMethod?.IsPublic == true, Type t => t.IsNestedPublic || t.IsNestedFamily, _ => false };
  if (!visible) continue;
  var detail = member is MethodBase method ? $" {method.Attributes} {string.Join(";", method.GetParameters().Select(p => $"{p.Name}:{p.Attributes}:{p.DefaultValue}"))}" : member is FieldInfo field ? $" {field.Attributes}" : "";
  var nulls = member switch {
   PropertyInfo p => NullInfo(nullable.Create(p)),
   FieldInfo f => NullInfo(nullable.Create(f)),
   MethodInfo m => NullInfo(nullable.Create(m.ReturnParameter)) + string.Join(";", m.GetParameters().Select(p => NullInfo(nullable.Create(p)))),
   ConstructorInfo c => string.Join(";", c.GetParameters().Select(p => NullInfo(nullable.Create(p)))),
   _ => ""
  };
  var contracts = Attributes(member.GetCustomAttributesData());
  if (member is MethodBase mb) contracts += string.Join(";", mb.GetParameters().Select(p => Attributes(p.GetCustomAttributesData())));
  lines.Add($"M {type.FullName} {member.MemberType} {member}{detail} nullability={nulls} contracts={contracts}");
 }
}
foreach (var line in lines.Order(StringComparer.Ordinal)) Console.WriteLine(line.Replace("ExcelRenderer.Slim", "ExcelRenderer.Core"));
"""


def run(*command):
    return subprocess.check_output([str(part) for part in command], text=True, stderr=subprocess.STDOUT)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline-worktree', type=pathlib.Path, required=True)
    args = parser.parse_args()
    baseline = args.baseline_worktree.resolve()
    output = ROOT / 'TestResults/CoreRefactor/API'
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='excel-api-') as temporary:
        work = pathlib.Path(temporary)
        project = work / 'snapshot.csproj'
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>')
        (work / 'Program.cs').write_text(SNAPSHOT_SOURCE)
        run('dotnet', 'restore', project, '--ignore-failed-sources', '-p:NuGetAudit=false')
        run('dotnet', 'build', project, '-c', 'Release', '--no-restore', '-m:1')
        executable = work / 'bin/Release/net10.0/snapshot.dll'
        dependencies = baseline / 'src/ExcelRenderer.Tool/bin/Release/net10.0'
        baseline_main = baseline / 'src/ExcelRenderer/bin/Release/netstandard2.1'
        results = {}
        for product, original, current in [
            ('main', baseline_main / 'ExcelRenderer.dll', ROOT / 'src/ExcelRenderer/bin/Release/netstandard2.1/ExcelRenderer.dll'),
            ('core', baseline / 'src/ExcelRenderer.Slim/bin/Release/netstandard2.1/ExcelRenderer.Slim.dll', ROOT / 'src/ExcelRenderer.Core/bin/Release/netstandard2.1/ExcelRenderer.Core.dll'),
        ]:
            snapshots = []
            for tag, assembly in [('old', original), ('new', current)]:
                text = run('dotnet', executable, assembly, dependencies)
                (output / f'{product}-{tag}.txt').write_text(text)
                snapshots.append(text.splitlines())
            difference = list(difflib.unified_diff(*snapshots))
            (output / f'{product}.diff').write_text('\n'.join(difference))
            results[product] = {'entries': len(snapshots[1]), 'difference_lines': len(difference)}
            print(product, results[product], flush=True)
            assert not difference, f'{product}: public contract changed; inspect {output}'
        (ROOT / 'docs/core-refactor-api-results.json').write_text(json.dumps({
            'checks': 'public/protected types and members, flags/defaults, semantic nullable states and code-analysis/obsolete/params contracts',
            'products': results,
        }, indent=2) + '\n')


if __name__ == '__main__':
    main()
