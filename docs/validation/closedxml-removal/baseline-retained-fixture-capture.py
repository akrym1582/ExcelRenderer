from pathlib import Path
import re
root=Path('/tmp/excelrenderer-closedxml-baseline')
for project in ['ExcelRenderer.Tests','ExcelRenderer.Mapping.Tests','ExcelRenderer.Tool.Tests']:
    folder=root/'tests'/project
    for path in folder.glob('*.cs'):
        if path.name=='FixtureCapture.cs':continue
        s=path.read_text()
        s=re.sub(r'\b(\w+)\.SaveAs\((\w+)\);',r'FixtureCapture.Save(\1, \2);',s)
        path.write_text(s)
    (folder/'FixtureCapture.cs').write_text('''using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
internal static class FixtureCapture
{
 private static readonly object Gate = new();
 internal static void Save(XLWorkbook workbook, Stream output, [CallerFilePath] string file="",[CallerMemberName] string member="",[CallerLineNumber]int line=0)
 {
  using var capture = new MemoryStream(); workbook.SaveAs(capture); Record(capture.ToArray(),file,member,line); capture.Position=0;capture.CopyTo(output);
 }
 internal static void Save(XLWorkbook workbook, string output, [CallerFilePath] string file="",[CallerMemberName] string member="",[CallerLineNumber]int line=0)
 {
  using var capture = new MemoryStream(); workbook.SaveAs(capture); Record(capture.ToArray(),file,member,line);File.WriteAllBytes(output,capture.ToArray());
 }
 private static void Record(byte[] data,string file,string member,int line)
 {
  var frames = new StackTrace().GetFrames();
  var test = frames.Select(frame=>frame.GetMethod()).FirstOrDefault(method=>method?.GetCustomAttributes(false).Any(attr=>attr is Xunit.FactAttribute)==true);
  var name = test is null ? member : test.Name;
  var type = test?.DeclaringType?.Name ?? Path.GetFileNameWithoutExtension(file);
  using var stream = new MemoryStream(data);using var zip = new System.IO.Compression.ZipArchive(stream);
  var canonical = string.Join("\\n",zip.Entries.Where(entry=>entry.FullName.StartsWith("xl/")&&!entry.FullName.EndsWith(".rels")).OrderBy(entry=>entry.FullName).Select(entry=>{using var reader=new StreamReader(entry.Open());return reader.ReadToEnd();}));
  var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).Substring(0,16);
  var key=type+"."+name+"."+hash+".xlsx";
  lock(Gate){Directory.CreateDirectory("/tmp/excelrenderer-captured-fixtures");File.WriteAllBytes("/tmp/excelrenderer-captured-fixtures/"+key,data);File.AppendAllText("/tmp/excelrenderer-captured-fixtures/manifest.jsonl",JsonSerializer.Serialize(new{key,type,name,file,member,line})+"\\n");}
 }
}
''')
