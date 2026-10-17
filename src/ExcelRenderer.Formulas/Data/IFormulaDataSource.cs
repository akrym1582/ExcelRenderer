using System;
using System.Collections.Generic;
using System.Globalization;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Values;

namespace ExcelRenderer.Formulas.Data
{
    /// <summary>Provides workbook data that must remain immutable throughout an evaluation session.</summary>
    public interface IFormulaDataSource
    {
        /// <summary>Resolves a display name to a stable sheet identifier.</summary>
        /// <param name="name">The name.</param>
        /// <param name="sheetId">The sheet id.</param>
        /// <returns>The resulting value.</returns>
        bool TryResolveSheet(string name, out string sheetId);

        /// <summary>Returns the constant, formula text or blank stored at an address. Invalid stable sheet identifiers must throw an argument exception.</summary>
        /// <param name="address">The address.</param>
        /// <returns>The resulting value.</returns>
        FormulaCellData GetCell(FormulaCellAddress address);

        /// <summary>Resolves a sheet-local name before a workbook name; explicit qualifiers use local names only.</summary>
        /// <param name="qualifiedSheet">The qualified sheet.</param>
        /// <param name="name">The name.</param>
        /// <param name="currentSheetId">The current sheet id.</param>
        /// <param name="range">The range.</param>
        /// <returns>The resulting value.</returns>
        bool TryResolveName(string? qualifiedSheet, string name, string currentSheetId, out FormulaRange range);
    }
}
