using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace ExcelRenderer.Formulas
{
    /// <summary>Provides an immutable sparse workbook snapshot.</summary>
    public sealed class InMemoryFormulaWorkbook : ISparseFormulaDataSource
    {
        private readonly Dictionary<string, string> sheets;
        private readonly Dictionary<FormulaCellAddress, FormulaCellData> cells;
        private readonly Dictionary<string, FormulaRange> names;
        private readonly FormulaRangeIndex index;

        private InMemoryFormulaWorkbook(Builder builder)
        {
            sheets = new Dictionary<string, string>(builder.Sheets, StringComparer.OrdinalIgnoreCase);
            cells = new Dictionary<FormulaCellAddress, FormulaCellData>(builder.Cells);
            names = new Dictionary<string, FormulaRange>(builder.Names, StringComparer.OrdinalIgnoreCase);
            index = new FormulaRangeIndex(cells.Where(pair => pair.Value.Formula != null || pair.Value.Value.Kind != FormulaValueKind.Blank).Select(pair => pair.Key));
        }

        /// <summary>Enumerates all stored values and formulas in row order.</summary>
        /// <param name="range">Inclusive rectangle.</param>
        /// <param name="cancellationToken">Cooperative cancellation.</param>
        /// <returns>Stored coordinates.</returns>
        public IEnumerable<FormulaCellAddress> EnumerateStoredCells(FormulaRange range, CancellationToken cancellationToken)
        {
            if (!sheets.ContainsKey(range.SheetId))
            {
                throw new ArgumentException("Unknown stable sheet identifier: " + range.SheetId, nameof(range));
            }

            return index.Enumerate(range, cancellationToken);
        }

        /// <summary>Resolves a display name to a stable sheet identifier.</summary>
        /// <param name="name">The name.</param>
        /// <param name="sheetId">The sheet id.</param>
        /// <returns>The resulting value.</returns>
        public bool TryResolveSheet(string name, out string sheetId) => sheets.TryGetValue(name, out sheetId!);

        /// <summary>Returns the constant, formula text or blank stored at an address.</summary>
        /// <param name="address">The address.</param>
        /// <returns>The resulting value.</returns>
        public FormulaCellData GetCell(FormulaCellAddress address)
        {
            if (!sheets.ContainsKey(address.SheetId))
            {
                throw new ArgumentException("Unknown stable sheet identifier: " + address.SheetId, nameof(address));
            }

            return cells.TryGetValue(address, out var value) ? value : FormulaCellData.Blank;
        }

        /// <summary>Resolves a sheet-local name before a workbook name; explicit qualifiers use local names only.</summary>
        /// <param name="qualifiedSheet">The qualified sheet.</param>
        /// <param name="name">The name.</param>
        /// <param name="currentSheetId">The current sheet id.</param>
        /// <param name="range">The range.</param>
        /// <returns>The resulting value.</returns>
        public bool TryResolveName(string? qualifiedSheet, string name, string currentSheetId, out FormulaRange range)
        {
            if (qualifiedSheet != null)
            {
                return names.TryGetValue(qualifiedSheet + "!" + name, out range);
            }

            return names.TryGetValue(currentSheetId + "!" + name, out range) || names.TryGetValue("!" + name, out range);
        }

        /// <summary>Builds immutable sparse workbook snapshots with duplicate registration checks.</summary>
        public sealed class Builder
        {
            /// <summary>Gets builder storage.</summary>
            internal Dictionary<string, string> Sheets { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Gets builder storage.</summary>
            internal Dictionary<FormulaCellAddress, FormulaCellData> Cells { get; } = new Dictionary<FormulaCellAddress, FormulaCellData>();

            /// <summary>Gets builder storage.</summary>
            internal Dictionary<string, FormulaRange> Names { get; } = new Dictionary<string, FormulaRange>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Registers a sheet, rejecting case-insensitive duplicates.</summary>
            /// <param name="name">The name.</param>
            /// <returns>The resulting value.</returns>
            public Builder AddSheet(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ArgumentException("Sheet name is required.", nameof(name));
                }

                Sheets.Add(name, name);
                return this;
            }

            /// <summary>Registers a constant cell, rejecting duplicate assignments.</summary>
            /// <param name="address">The address.</param>
            /// <param name="value">The value.</param>
            /// <returns>The resulting value.</returns>
            public Builder SetValue(FormulaCellAddress address, FormulaValue value)
            {
                CheckSheet(address.SheetId);
                Cells.Add(address, FormulaCellData.FromValue(value));
                return this;
            }

            /// <summary>Registers formula text without a saved result, rejecting duplicate assignments.</summary>
            /// <param name="address">The address.</param>
            /// <param name="formula">The formula.</param>
            /// <returns>The resulting value.</returns>
            public Builder SetFormula(FormulaCellAddress address, string formula)
            {
                CheckSheet(address.SheetId);
                Cells.Add(address, FormulaCellData.FromFormula(formula));
                return this;
            }

            /// <summary>Registers an absolute rectangular name, optionally scoped to one sheet.</summary>
            /// <param name="name">The name.</param>
            /// <param name="range">The range.</param>
            /// <param name="localSheet">The local sheet.</param>
            /// <returns>The resulting value.</returns>
            public Builder AddName(string name, FormulaRange range, string? localSheet = null)
            {
                if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_'))
                {
                    throw new ArgumentException("Invalid name.", nameof(name));
                }

                foreach (var c in name)
                {
                    if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.'))
                    {
                        throw new ArgumentException("Invalid name.", nameof(name));
                    }
                }

                CheckSheet(range.SheetId);
                if (localSheet != null)
                {
                    CheckSheet(localSheet);
                }

                Names.Add((localSheet ?? string.Empty) + "!" + name, range);
                return this;
            }

            /// <summary>Copies the builder into an immutable workbook snapshot.</summary>
            /// <returns>The resulting value.</returns>
            public InMemoryFormulaWorkbook Build() => new InMemoryFormulaWorkbook(this);

            private void CheckSheet(string sheet)
            {
                if (!Sheets.ContainsKey(sheet))
                {
                    throw new ArgumentException("Unknown sheet: " + sheet);
                }
            }
        }
    }
}
