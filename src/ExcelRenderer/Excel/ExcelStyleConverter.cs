using ExcelRenderer.Model;
using ExcelRenderer.OpenXml;

namespace ExcelRenderer.Excel;

/// <summary>Converts resolved Open XML styles to rendering styles.</summary>
public static class ExcelStyleConverter
{
    /// <summary>Converts font, fill, borders and alignment without reading or calculating a cell.</summary>
    /// <param name="style">The resolved style returned by OpenXmlWorkbookReader.ResolveStyle.</param>
    /// <param name="valueType">The stored value type, including a formula's cached result type, used to resolve General alignment.</param>
    /// <returns>The original source appearance with General alignment resolved.</returns>
    /// <exception cref="ArgumentNullException">The style is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value type is unknown.</exception>
    public static CellStyle Convert(CellStyleInfo style, CellValueType valueType)
    {
        if (style is null)
        {
            throw new ArgumentNullException(nameof(style));
        }

        if (!Enum.IsDefined(typeof(CellValueType), valueType))
        {
            throw new ArgumentOutOfRangeException(nameof(valueType));
        }

        return CoreIntegration.CoreModelAdapter.ToPublic(Core.Excel.OpenXmlStyleConverter.Convert(style, valueType));
    }
}
