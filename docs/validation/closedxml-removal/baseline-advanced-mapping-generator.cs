using ClosedXML.Excel;
using ExcelRenderer.Mapping;
using System.Text.Json;
var destination="/workspace/ExcelRenderer/tests/ExcelRenderer.Mapping.Tests/Fixtures/ClosedXmlBaseline";
var results=new List<object>();
foreach(var feature in new[]{"pivot","pivot-values","sparkline"})
{
 using var workbook=new XLWorkbook();var sheet=workbook.AddWorksheet("Template");
 sheet.Cell("A1").Value="Header";sheet.Cell("A2").Value="**items[*]";sheet.Cell("A3").Value="Footer";
 sheet.Cell("A6").Value="Category";sheet.Cell("B6").Value="Amount";sheet.Cell("A7").Value="One";sheet.Cell("B7").Value=feature=="pivot-values" ? (XLCellValue)"**amount" : 10;sheet.Cell("A8").Value="Two";sheet.Cell("B8").Value=20;
 if(feature=="sparkline"){sheet.SparklineGroups.Add(sheet.Cell("D7"),sheet.Range("B7:B8"));}
 else{var pivot=sheet.PivotTables.Add("Summary",sheet.Cell("E6"),sheet.Range("A6:B8"));pivot.RowLabels.Add("Category");pivot.Values.Add("Amount");}
 var path=Path.Combine(destination,feature+".xlsx");workbook.SaveAs(path);
 foreach(var count in new[]{0,3})
 {
  string? error=null;try{ExcelTemplateMapper.Map(path,Path.Combine(destination,feature+"-expected-"+count+".xlsx"),new{items=Enumerable.Range(1,count).ToArray(),amount=99});}catch(Exception ex){error=ex.GetType().Name+": "+ex.Message;}
  results.Add(new{Feature=feature,Count=count,Error=error});
 }
}
File.WriteAllText(Path.Combine(destination,"advanced-baseline.json"),JsonSerializer.Serialize(new{BaselineCommit="24c9dea4d67190951d152eef56f76abc943ef733",Results=results},new JsonSerializerOptions{WriteIndented=true}));
