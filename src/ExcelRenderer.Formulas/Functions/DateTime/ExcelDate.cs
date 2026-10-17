using System;

namespace ExcelRenderer.Formulas
{
    /// <summary>Represents calendar components, including the fictitious 1900 leap day and day zero.</summary>
    internal readonly struct ExcelDate
    {
        /// <summary>Initializes a new instance of the <see cref="ExcelDate"/> struct.</summary>
        /// <param name="year">Calendar year.</param>
        /// <param name="month">Calendar month.</param>
        /// <param name="day">Calendar day.</param>
        internal ExcelDate(int year, int month, int day)
        {
            Year = year;
            Month = month;
            Day = day;
        }

        /// <summary>Gets the calendar year.</summary>
        internal int Year { get; }

        /// <summary>Gets the calendar month.</summary>
        internal int Month { get; }

        /// <summary>Gets the calendar day.</summary>
        internal int Day { get; }

        /// <summary>Returns the maximum supported integer serial.</summary>
        /// <param name="system">Workbook date system.</param>
        /// <returns>The serial of 9999-12-31.</returns>
        internal static double Maximum(FormulaDateSystem system) => system == FormulaDateSystem.Excel1904 ? 2957003 : 2958465;

        /// <summary>Converts a serial without losing the fictitious leap day.</summary>
        /// <param name="serial">Numeric date including any time fraction.</param>
        /// <param name="system">Workbook date system.</param>
        /// <param name="date">Calendar result.</param>
        /// <returns>Whether the serial is within range.</returns>
        internal static bool TryRead(double serial, FormulaDateSystem system, out ExcelDate date)
        {
            date = default;
            if (serial < 0 || Math.Floor(serial) > Maximum(system))
            {
                return false;
            }

            serial = Math.Floor(serial);
            if (system == FormulaDateSystem.Excel1900 && (serial == 0 || serial == 60))
            {
                date = serial == 0 ? new ExcelDate(1900, 1, 0) : new ExcelDate(1900, 2, 29);
                return true;
            }

            var epoch = system == FormulaDateSystem.Excel1904 ? new DateTime(1904, 1, 1) : new DateTime(1899, 12, 31);
            var value = epoch.AddDays(serial - (system == FormulaDateSystem.Excel1900 && serial > 60 ? 1 : 0));
            date = new ExcelDate(value.Year, value.Month, value.Day);
            return true;
        }

        /// <summary>Converts calendar components to an Excel serial.</summary>
        /// <param name="year">Calendar year.</param>
        /// <param name="month">Calendar month.</param>
        /// <param name="day">Calendar day.</param>
        /// <param name="system">Workbook date system.</param>
        /// <returns>The integer serial.</returns>
        internal static double Serial(int year, int month, int day, FormulaDateSystem system)
        {
            if (system == FormulaDateSystem.Excel1900 && year == 1900 && month == 2 && day == 29)
            {
                return 60;
            }

            var first = new DateTime(year, month, 1);
            var epoch = system == FormulaDateSystem.Excel1904 ? new DateTime(1904, 1, 1) : new DateTime(1899, 12, 31);
            return (first - epoch).Days + (system == FormulaDateSystem.Excel1900 && first >= new DateTime(1900, 3, 1) ? 1 : 0) + day - 1;
        }

        /// <summary>Returns month length, including February 1900 in the 1900 system.</summary>
        /// <param name="year">Calendar year.</param>
        /// <param name="month">Calendar month.</param>
        /// <param name="system">Workbook date system.</param>
        /// <returns>The number of days.</returns>
        internal static int DaysInMonth(int year, int month, FormulaDateSystem system) => system == FormulaDateSystem.Excel1900 && year == 1900 && month == 2 ? 29 : DateTime.DaysInMonth(year, month);
    }
}
