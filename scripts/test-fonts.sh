#!/usr/bin/env bash
# Source this script before dotnet test; it confines Fontconfig to committed test assets.
_excelrenderer_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export FONTCONFIG_FILE="$_excelrenderer_root/TestResults/fontconfig/fonts.conf"
mkdir -p "$(dirname "$FONTCONFIG_FILE")/cache"
python3 - "$_excelrenderer_root" "$FONTCONFIG_FILE" <<'PY'
import pathlib, sys
from xml.sax.saxutils import escape
root, output = map(pathlib.Path, sys.argv[1:])
output.write_text('''<?xml version="1.0"?>
<!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd">
<fontconfig>
  <dir>''' + escape(str(root / 'third_party/NotoSansJP')) + '''</dir>
  <dir>''' + escape(str(root / 'tests/ExcelRenderer.Tests/Fonts')) + '''</dir>
  <cachedir>''' + escape(str(output.parent / 'cache')) + '''</cachedir>
  <alias><family>sans-serif</family><prefer><family>Noto Sans JP</family></prefer></alias>
</fontconfig>
''')
PY
fc-cache -f
unset _excelrenderer_root
