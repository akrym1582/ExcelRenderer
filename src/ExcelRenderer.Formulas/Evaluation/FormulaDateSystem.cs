namespace ExcelRenderer.Formulas.Evaluation
{
    /// <summary>Identifies the workbook's Excel date epoch.</summary>
    public enum FormulaDateSystem
    {
        /// <summary>Serial 1 is 1900-01-01; serial 60 is the fictitious leap day.</summary>
        Excel1900,

        /// <summary>Serial zero is 1904-01-01.</summary>
        Excel1904,
    }
}
