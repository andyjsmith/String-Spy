using System;

namespace StringsApp.Strings;

public abstract class OffsetFormatter
{
    public static readonly OffsetFormatter Hexadecimal = new HexadecimalOffsetFormatter();
    public static readonly OffsetFormatter Decimal = new DecimalOffsetFormatter();
    public static readonly OffsetFormatter Octal = new OctalOffsetFormatter();

    public abstract string Name { get; }
    public abstract string Format(long offset);

    class HexadecimalOffsetFormatter : OffsetFormatter
    {
        public override string Name => "Hexadecimal";
        public override string Format(long offset) => "0x" + offset.ToString("X");
    }

    class DecimalOffsetFormatter : OffsetFormatter
    {
        public override string Name => "Decimal";
        public override string Format(long offset) => offset.ToString();
    }

    class OctalOffsetFormatter : OffsetFormatter
    {
        public override string Name => "Octal";
        public override string Format(long offset) => "0" + Convert.ToString(offset, 8);
    }
}