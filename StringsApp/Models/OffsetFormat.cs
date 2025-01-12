using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StringsApp.Models;

public enum OffsetFormat
{
    Hexadecimal,
    Decimal,
    Octal,
    Binary
}

public abstract class OffsetFormatter
{
    public static readonly OffsetFormatter Hexadecimal = new HexadecimalOffsetFormatter();
    public static readonly OffsetFormatter Decimal = new DecimalOffsetFormatter();
    public static readonly OffsetFormatter Octal = new OctalOffsetFormatter();

    public abstract string Name { get; }
    public abstract string Format(long offset);

    class HexadecimalOffsetFormatter : OffsetFormatter
    {
        public override string Name => "Hex";
        public override string Format(long offset) => "0x" + offset.ToString("X");
    }

    class DecimalOffsetFormatter : OffsetFormatter
    {
        public override string Name => "Dec";
        public override string Format(long offset) => offset.ToString();
    }

    class OctalOffsetFormatter : OffsetFormatter
    {
        public override string Name => "Oct";
        public override string Format(long offset) => "0" + Convert.ToString(offset, 8);
    }
}

public class OffsetFormatToShortStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not OffsetFormat format) return null;
        return format switch
        {
            OffsetFormat.Hexadecimal => "Hex",
            OffsetFormat.Decimal => "Dec",
            OffsetFormat.Octal => "Oct",
            OffsetFormat.Binary => "Bin",
            _ => null
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public class OffsetFormatToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not OffsetFormat format ? null : format.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}