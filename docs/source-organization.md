# Source organization

Public namespaces, type names and assembly boundaries are unchanged. Input/ and Properties/ retain their existing roles. Historical validation inventories describe their recorded SHA and are not rewritten.

| Previous file | Current file |
| --- | --- |
| `src/ExcelRenderer.Formulas/DecimalRounding.cs` | `src/ExcelRenderer.Formulas/Functions/Numeric/DecimalRounding.cs` |
| `src/ExcelRenderer.Formulas/FormulaCellAddress.cs` | `src/ExcelRenderer.Formulas/References/FormulaCellAddress.cs` |
| `src/ExcelRenderer.Formulas/FormulaCellData.cs` | `src/ExcelRenderer.Formulas/Values/FormulaCellData.cs` |
| `src/ExcelRenderer.Formulas/FormulaDiagnostic.cs` | `src/ExcelRenderer.Formulas/Diagnostics/FormulaDiagnostic.cs` |
| `src/ExcelRenderer.Formulas/FormulaEngine.cs` | `src/ExcelRenderer.Formulas/Evaluation/FormulaEngine.cs` |
| `src/ExcelRenderer.Formulas/FormulaEvaluationOptions.cs` | `src/ExcelRenderer.Formulas/Evaluation/FormulaEvaluationOptions.cs` |
| `src/ExcelRenderer.Formulas/FormulaEvaluationResult.cs` | `src/ExcelRenderer.Formulas/Evaluation/FormulaEvaluationResult.cs` |
| `src/ExcelRenderer.Formulas/FormulaNode.cs` | `src/ExcelRenderer.Formulas/Syntax/FormulaNode.cs` |
| `src/ExcelRenderer.Formulas/FormulaNodeKind.cs` | `src/ExcelRenderer.Formulas/Syntax/FormulaNodeKind.cs` |
| `src/ExcelRenderer.Formulas/FormulaParseResult.cs` | `src/ExcelRenderer.Formulas/Syntax/FormulaParseResult.cs` |
| `src/ExcelRenderer.Formulas/FormulaParser.cs` | `src/ExcelRenderer.Formulas/Syntax/FormulaParser.cs` |
| `src/ExcelRenderer.Formulas/FormulaRange.cs` | `src/ExcelRenderer.Formulas/References/FormulaRange.cs` |
| `src/ExcelRenderer.Formulas/FormulaRewriter.cs` | `src/ExcelRenderer.Formulas/References/FormulaRewriter.cs` |
| `src/ExcelRenderer.Formulas/FormulaSession.cs` | `src/ExcelRenderer.Formulas/Evaluation/FormulaSession.cs` |
| `src/ExcelRenderer.Formulas/FormulaValue.cs` | `src/ExcelRenderer.Formulas/Values/FormulaValue.cs` |
| `src/ExcelRenderer.Formulas/FormulaValueKind.cs` | `src/ExcelRenderer.Formulas/Values/FormulaValueKind.cs` |
| `src/ExcelRenderer.Formulas/IFormulaDataSource.cs` | `src/ExcelRenderer.Formulas/Data/IFormulaDataSource.cs` |
| `src/ExcelRenderer.Formulas/InMemoryFormulaWorkbook.cs` | `src/ExcelRenderer.Formulas/Data/InMemoryFormulaWorkbook.cs` |
| `src/ExcelRenderer.Formulas/ParsedFormula.cs` | `src/ExcelRenderer.Formulas/Syntax/ParsedFormula.cs` |
| `src/ExcelRenderer.Formulas/SourceSpan.cs` | `src/ExcelRenderer.Formulas/Syntax/SourceSpan.cs` |
| `src/ExcelRenderer.OpenXml/CellAddress.cs` | `src/ExcelRenderer.OpenXml/Model/Cells/CellAddress.cs` |
| `src/ExcelRenderer.OpenXml/CellDisplayResult.cs` | `src/ExcelRenderer.OpenXml/Formatting/CellDisplayResult.cs` |
| `src/ExcelRenderer.OpenXml/CellRange.cs` | `src/ExcelRenderer.OpenXml/Model/Cells/CellRange.cs` |
| `src/ExcelRenderer.OpenXml/CellRecord.cs` | `src/ExcelRenderer.OpenXml/Model/Cells/CellRecord.cs` |
| `src/ExcelRenderer.OpenXml/CellStyleInfo.cs` | `src/ExcelRenderer.OpenXml/Model/Styles/CellStyleInfo.cs` |
| `src/ExcelRenderer.OpenXml/CellValue.cs` | `src/ExcelRenderer.OpenXml/Model/Cells/CellValue.cs` |
| `src/ExcelRenderer.OpenXml/CellValueEdit.cs` | `src/ExcelRenderer.OpenXml/Writing/Plans/CellValueEdit.cs` |
| `src/ExcelRenderer.OpenXml/CellValueType.cs` | `src/ExcelRenderer.OpenXml/Model/Cells/CellValueType.cs` |
| `src/ExcelRenderer.OpenXml/ColumnInfo.cs` | `src/ExcelRenderer.OpenXml/Model/Workbook/ColumnInfo.cs` |
| `src/ExcelRenderer.OpenXml/DefinedNameInfo.cs` | `src/ExcelRenderer.OpenXml/Model/Workbook/DefinedNameInfo.cs` |
| `src/ExcelRenderer.OpenXml/ElementInfo.cs` | `src/ExcelRenderer.OpenXml/Model/Workbook/ElementInfo.cs` |
| `src/ExcelRenderer.OpenXml/FontInfo.cs` | `src/ExcelRenderer.OpenXml/Model/Styles/FontInfo.cs` |
| `src/ExcelRenderer.OpenXml/FormulaCalculationException.cs` | `src/ExcelRenderer.OpenXml/Calculation/FormulaCalculationException.cs` |
| `src/ExcelRenderer.OpenXml/FormulaCalculationMode.cs` | `src/ExcelRenderer.OpenXml/Calculation/FormulaCalculationMode.cs` |
| `src/ExcelRenderer.OpenXml/FormulaInfo.cs` | `src/ExcelRenderer.OpenXml/Model/Cells/FormulaInfo.cs` |
| `src/ExcelRenderer.OpenXml/FormulaTextResolver.cs` | `src/ExcelRenderer.OpenXml/Calculation/FormulaTextResolver.cs` |
| `src/ExcelRenderer.OpenXml/IWorkbookDataReader.cs` | `src/ExcelRenderer.OpenXml/Reading/IWorkbookDataReader.cs` |
| `src/ExcelRenderer.OpenXml/ImageInfo.cs` | `src/ExcelRenderer.OpenXml/Model/Drawing/ImageInfo.cs` |
| `src/ExcelRenderer.OpenXml/LeaveOpenStream.cs` | `src/ExcelRenderer.OpenXml/Infrastructure/LeaveOpenStream.cs` |
| `src/ExcelRenderer.OpenXml/NumberFormats.cs` | `src/ExcelRenderer.OpenXml/Formatting/NumberFormats.cs` |
| `src/ExcelRenderer.OpenXml/OpenXmlReaderOptions.cs` | `src/ExcelRenderer.OpenXml/Reading/OpenXmlReaderOptions.cs` |
| `src/ExcelRenderer.OpenXml/OpenXmlWorkbookReader.Cells.cs` | `src/ExcelRenderer.OpenXml/Reading/OpenXmlWorkbookReader.Cells.cs` |
| `src/ExcelRenderer.OpenXml/OpenXmlWorkbookReader.Images.cs` | `src/ExcelRenderer.OpenXml/Reading/OpenXmlWorkbookReader.Images.cs` |
| `src/ExcelRenderer.OpenXml/OpenXmlWorkbookReader.Metadata.cs` | `src/ExcelRenderer.OpenXml/Reading/OpenXmlWorkbookReader.Metadata.cs` |
| `src/ExcelRenderer.OpenXml/OpenXmlWorkbookReader.cs` | `src/ExcelRenderer.OpenXml/Reading/OpenXmlWorkbookReader.cs` |
| `src/ExcelRenderer.OpenXml/OpenXmlWorkbookWriter.Geometry.cs` | `src/ExcelRenderer.OpenXml/Writing/OpenXmlWorkbookWriter.Geometry.cs` |
| `src/ExcelRenderer.OpenXml/OpenXmlWorkbookWriter.Pivots.cs` | `src/ExcelRenderer.OpenXml/Writing/OpenXmlWorkbookWriter.Pivots.cs` |
| `src/ExcelRenderer.OpenXml/OpenXmlWorkbookWriter.Structures.cs` | `src/ExcelRenderer.OpenXml/Writing/OpenXmlWorkbookWriter.Structures.cs` |
| `src/ExcelRenderer.OpenXml/OpenXmlWorkbookWriter.cs` | `src/ExcelRenderer.OpenXml/Writing/OpenXmlWorkbookWriter.cs` |
| `src/ExcelRenderer.OpenXml/OperationMetrics.cs` | `src/ExcelRenderer.OpenXml/Diagnostics/OperationMetrics.cs` |
| `src/ExcelRenderer.OpenXml/OutputRowOccurrence.cs` | `src/ExcelRenderer.OpenXml/Writing/Plans/OutputRowOccurrence.cs` |
| `src/ExcelRenderer.OpenXml/PrintRangeInfo.cs` | `src/ExcelRenderer.OpenXml/Model/Printing/PrintRangeInfo.cs` |
| `src/ExcelRenderer.OpenXml/PrintReferenceInfo.cs` | `src/ExcelRenderer.OpenXml/Model/Printing/PrintReferenceInfo.cs` |
| `src/ExcelRenderer.OpenXml/PrintReferences.cs` | `src/ExcelRenderer.OpenXml/References/PrintReferences.cs` |
| `src/ExcelRenderer.OpenXml/ReaderDiagnostic.cs` | `src/ExcelRenderer.OpenXml/Diagnostics/ReaderDiagnostic.cs` |
| `src/ExcelRenderer.OpenXml/ReaderDiagnosticCodes.cs` | `src/ExcelRenderer.OpenXml/Diagnostics/ReaderDiagnosticCodes.cs` |
| `src/ExcelRenderer.OpenXml/RichTextRunInfo.cs` | `src/ExcelRenderer.OpenXml/Model/Cells/RichTextRunInfo.cs` |
| `src/ExcelRenderer.OpenXml/RowInfo.cs` | `src/ExcelRenderer.OpenXml/Model/Workbook/RowInfo.cs` |
| `src/ExcelRenderer.OpenXml/RowPrototypeCache.cs` | `src/ExcelRenderer.OpenXml/Writing/RowPrototypeCache.cs` |
| `src/ExcelRenderer.OpenXml/SheetEditPlan.cs` | `src/ExcelRenderer.OpenXml/Writing/Plans/SheetEditPlan.cs` |
| `src/ExcelRenderer.OpenXml/SheetInfo.cs` | `src/ExcelRenderer.OpenXml/Model/Workbook/SheetInfo.cs` |
| `src/ExcelRenderer.OpenXml/StructuralReferences.Formulas.cs` | `src/ExcelRenderer.OpenXml/References/StructuralReferences.Formulas.cs` |
| `src/ExcelRenderer.OpenXml/StructuralReferences.cs` | `src/ExcelRenderer.OpenXml/References/StructuralReferences.cs` |
| `src/ExcelRenderer.OpenXml/StyleTable.cs` | `src/ExcelRenderer.OpenXml/Formatting/StyleTable.cs` |
| `src/ExcelRenderer.OpenXml/WorkbookCalculationSession.cs` | `src/ExcelRenderer.OpenXml/Calculation/WorkbookCalculationSession.cs` |
| `src/ExcelRenderer.OpenXml/WorkbookEditPlan.cs` | `src/ExcelRenderer.OpenXml/Writing/Plans/WorkbookEditPlan.cs` |
| `src/ExcelRenderer.OpenXml/WorkbookFormulaInspection.cs` | `src/ExcelRenderer.OpenXml/Calculation/WorkbookFormulaInspection.cs` |
| `src/ExcelRenderer.OpenXml/WorkbookInfo.cs` | `src/ExcelRenderer.OpenXml/Model/Workbook/WorkbookInfo.cs` |
| `src/ExcelRenderer.OpenXml/WorkbookRecalculator.cs` | `src/ExcelRenderer.OpenXml/Calculation/WorkbookRecalculator.cs` |
| `src/ExcelRenderer.OpenXml/WorkbookRelationships.cs` | `src/ExcelRenderer.OpenXml/Infrastructure/WorkbookRelationships.cs` |
| `src/ExcelRenderer.OpenXml/WorkbookWriteCapabilities.cs` | `src/ExcelRenderer.OpenXml/Writing/WorkbookWriteCapabilities.cs` |
| `src/ExcelRenderer.OpenXml/WorksheetInfo.cs` | `src/ExcelRenderer.OpenXml/Model/Workbook/WorksheetInfo.cs` |
| `src/ExcelRenderer.OpenXml/XmlValues.cs` | `src/ExcelRenderer.OpenXml/Infrastructure/XmlValues.cs` |

## Added implementation files

| Folder | Added responsibilities |
| --- | --- |
| Formulas/Data | ISparseFormulaDataSource and FormulaRangeIndex; the original three-method data source remains unchanged. |
| Formulas/Evaluation | Function dispatch, checked range cursors and session-local statistics. |
| Formulas/Functions | One registry; Numeric, Aggregation, Logical, Lookup, DateTime and Text partial implementations preserve private session control; bounded LookupIndex keeps typed first positions and ordered text positions. |
| Formulas/Criteria | Compiled criteria/coercion profile and bounded wildcard tokens. |
| OpenXml/Calculation | Sparse snapshot and workbook date-system/ISO adaptation in WorkbookCalculationSession. |
| tools/ExcelRenderer.Formulas.Oracle | Development-only typed input generator, Excel cache extractor, provenance comparer and engine/OpenXml/Mapping runner. |

No runtime package was added to Formulas. Active scripts/project paths were searched after moving; historical SHA inventories and logs remain unchanged.
