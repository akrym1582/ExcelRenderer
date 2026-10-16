using System.Text.Json;using System.Security.Cryptography;
var root="/workspace/ExcelRenderer";
var files=Directory.GetFiles(root+"/samples","*.xlsx",SearchOption.AllDirectories).Concat(Directory.GetFiles(root+"/tests/ExcelRenderer.Tests/SampleInputs","*.xlsx",SearchOption.AllDirectories)).Order().Select(path=>new { File=Path.GetRelativePath(root,path),Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),Projection=Comparison.Project(path)}).ToArray();
File.WriteAllText(root+"/docs/validation/closedxml-removal/raw-reader-baseline.json",JsonSerializer.Serialize(new {BaselineCommit="24c9dea4d67190951d152eef56f76abc943ef733",Files=files}));
