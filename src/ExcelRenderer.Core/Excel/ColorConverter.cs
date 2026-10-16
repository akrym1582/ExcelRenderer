namespace ExcelRenderer.Core.Excel;

/// <summary>Converts source RGB colors using the product tint arithmetic.</summary>
internal static class ColorConverter
{
    /// <summary>Applies the existing product tint arithmetic to source RGB.</summary>
    /// <param name="red">Base red component.</param>
    /// <param name="green">Base green component.</param>
    /// <param name="blue">Base blue component.</param>
    /// <param name="tint">Source theme tint.</param>
    /// <returns>Product RGB components.</returns>
    internal static (byte Red, byte Green, byte Blue) ApplyTint(byte red, byte green, byte blue, double tint)
    {
        if (tint == 0)
        {
            return (red, green, blue);
        }

        var normalizedRed = red / 255d;
        var normalizedGreen = green / 255d;
        var normalizedBlue = blue / 255d;
        var maximum = Math.Max(normalizedRed, Math.Max(normalizedGreen, normalizedBlue));
        var minimum = Math.Min(normalizedRed, Math.Min(normalizedGreen, normalizedBlue));
        var luminance = (maximum + minimum) / 2;
        var saturation = 0d;
        var hue = 0d;

        if (maximum != minimum)
        {
            var difference = maximum - minimum;
            saturation = luminance > 0.5
                ? difference / (2 - maximum - minimum)
                : difference / (maximum + minimum);

            hue = maximum == normalizedRed
                ? ((normalizedGreen - normalizedBlue) / difference) + (normalizedGreen < normalizedBlue ? 6 : 0)
                : maximum == normalizedGreen
                    ? ((normalizedBlue - normalizedRed) / difference) + 2
                    : ((normalizedRed - normalizedGreen) / difference) + 4;
            hue /= 6;
        }

        luminance = tint < 0
            ? luminance * (1 + tint)
            : (luminance * (1 - tint)) + tint;
        luminance = Math.Max(0, Math.Min(1, luminance));

        if (saturation == 0)
        {
            var component = ToByte(luminance);
            return (component, component, component);
        }

        var second = luminance < 0.5
            ? luminance * (1 + saturation)
            : luminance + saturation - (luminance * saturation);
        var first = (2 * luminance) - second;
        return (
            ToByte(HueToRgb(first, second, hue + (1d / 3))),
            ToByte(HueToRgb(first, second, hue)),
            ToByte(HueToRgb(first, second, hue - (1d / 3))));
    }

    private static double HueToRgb(double first, double second, double hue)
    {
        if (hue < 0)
        {
            hue += 1;
        }

        if (hue > 1)
        {
            hue -= 1;
        }

        if (hue < 1d / 6)
        {
            return first + ((second - first) * 6 * hue);
        }

        if (hue < 1d / 2)
        {
            return second;
        }

        if (hue < 2d / 3)
        {
            return first + ((second - first) * ((2d / 3) - hue) * 6);
        }

        return first;
    }

    private static byte ToByte(double component) =>
        (byte)Math.Round(Math.Max(0, Math.Min(255, component * 255)));
}
