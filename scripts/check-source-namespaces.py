#!/usr/bin/env python3
"""Keep declared namespaces aligned with project roots and physical folders."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
# The C# compiler recognizes this polyfill only by its BCL-qualified name.
COMPILER_SHIMS = {
    'src/ExcelRenderer/Compatibility/IsExternalInit.cs': 'System.Runtime.CompilerServices',
    'src/ExcelRenderer.Core/Compatibility/IsExternalInit.cs': 'System.Runtime.CompilerServices',
}


def main():
    failures, checked, shims = [], 0, set()
    for directory in ['src', 'tests', 'samples', 'tools']:
        for project in sorted((ROOT / directory).glob('*/*.csproj')):
            project_namespace = ET.parse(project).findtext('.//RootNamespace') or project.stem
            for path in sorted(project.parent.rglob('*.cs')):
                relative = path.relative_to(project.parent)
                if {'bin', 'obj'} & set(relative.parts):
                    continue
                declared = re.findall(r'^namespace\s+([\w.]+)\s*[;{]', path.read_text(encoding='utf-8'), re.MULTILINE)
                if not declared:
                    # Top-level entry points and assembly attributes need no namespace.
                    continue
                key = path.relative_to(ROOT).as_posix()
                expected = '.'.join((project_namespace, *relative.parent.parts))
                if key in COMPILER_SHIMS:
                    expected = COMPILER_SHIMS[key]
                    shims.add(key)
                for namespace in declared:
                    checked += 1
                    if namespace != expected:
                        failures.append(f'{key}: {namespace} (expected {expected})')
    assert shims == set(COMPILER_SHIMS), 'Review the explicit compiler-shim exceptions'
    assert not failures, '\n'.join(failures)
    print(f'{checked} namespace declarations match source folders; {len(shims)} required compiler shims verified')


if __name__ == '__main__':
    main()
