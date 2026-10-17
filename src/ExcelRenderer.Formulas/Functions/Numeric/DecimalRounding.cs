using System;
using System.Globalization;
using System.Numerics;

namespace ExcelRenderer.Formulas.Functions.Numeric;

/// <summary>Rounds the shortest round-trip decimal representation of a finite double.</summary>
internal static class DecimalRounding
{
    /// <summary>Rounds decimal digits without scaling the binary floating-point value.</summary>
    /// <param name="number">Finite input.</param>
    /// <param name="digits">Decimal places, truncated toward zero.</param>
    /// <param name="mode">ROUND, ROUNDUP or ROUNDDOWN.</param>
    /// <returns>The rounded value, or infinity when the decimal result exceeds double.</returns>
    internal static double Round(double number, double digits, string mode)
    {
        digits = Math.Truncate(digits);
        if (number == 0 || (digits >= 0 && number == Math.Truncate(number)))
        {
            return number;
        }

        string text = Math.Abs(number).ToString("R", CultureInfo.InvariantCulture);
        int exponent = 0;
        int exponentIndex = text.IndexOf('E');
        if (exponentIndex >= 0)
        {
            exponent = int.Parse(text.Substring(exponentIndex + 1), CultureInfo.InvariantCulture);
            text = text.Substring(0, exponentIndex);
        }

        int point = text.IndexOf('.');
        if (point >= 0)
        {
            exponent -= text.Length - point - 1;
            text = text.Remove(point, 1);
        }

        text = text.TrimStart('0');
        if (digits >= -exponent)
        {
            return number;
        }

        // Compare before converting digits to int or constructing powers. Even 1E100
        // places uses only bounded work; a double has at most 17 significant digits.
        double removedDigits = -digits - exponent;
        BigInteger rounded;
        if (removedDigits > text.Length)
        {
            if (mode != "ROUNDUP")
            {
                return digits < -308 ? 0 : Math.Sign(number) * 0d;
            }

            if (digits < -308)
            {
                return Math.Sign(number) * double.PositiveInfinity;
            }

            rounded = BigInteger.One;
        }
        else
        {
            var coefficient = BigInteger.Parse(text, CultureInfo.InvariantCulture);
            var divisor = BigInteger.Pow(10, (int)removedDigits);
            rounded = BigInteger.DivRem(coefficient, divisor, out var remainder);
            if ((mode == "ROUNDUP" && !remainder.IsZero) || (mode == "ROUND" && remainder * 2 >= divisor))
            {
                rounded++;
            }
        }

        if (rounded.IsZero)
        {
            // Keep the existing signed-zero convention; the old extreme-place
            // shortcut returned positive zero below -308 places.
            return digits < -308 ? 0 : Math.Sign(number) * 0d;
        }

        string result = rounded.ToString(CultureInfo.InvariantCulture) + "E" + (-digits).ToString("0", CultureInfo.InvariantCulture);
        return Math.Sign(number) * double.Parse(result, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
