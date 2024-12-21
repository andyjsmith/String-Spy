using System;
using System.Text;

namespace StringsApp.Strings;

public record StringResult(string Content, long Position)
{
    public long EndPosition => Position + Content.Length;
    public long Length => Content.Length;
}

public static class Character
{
    /// <summary>
    /// What set of characters should be used when finding strings, after being decoded using the current encoding.
    /// </summary>
    public enum CharSet
    {
        /// <summary>
        /// ASCII characters excluding control
        /// </summary>
        Ascii,

        /// <summary>
        /// ISO-8859-1 (Latin-1) characters excluding control
        /// </summary>
        Latin1,

        /// <summary>
        /// All valid characters in the currently selected encoding
        /// </summary>
        CurrentEncoding
    }

    public static CharSet StringToCharSet(string name)
    {
        return name switch
        {
            "Ascii" => CharSet.Ascii,
            "Latin1" => CharSet.Latin1,
            "CurrentEncoding" => CharSet.CurrentEncoding,
            _ => CharSet.Ascii
        };
    }

    public static Encoding StringToEncoding(string name)
    {
        try
        {
            return Encoding.GetEncoding(name);
        }
        catch (ArgumentException)
        {
            return Encoding.ASCII;
        }
    }

    private static bool IsControlOrNonPrintableWhiteSpace(char c, bool preserveNewLine = false)
    {
        if (c is ' ' or '\t') return false; // tab and space are whitespace, but we want to keep them
        if (preserveNewLine && c is '\r' or '\n') return false; // keep newlines if requested
        if (c is '\x7f' or < ' ' or >= '\x80' and <= '\x9f') return true;
        if (char.IsWhiteSpace(c)) return true;
        return false;
    }

    private static bool IsPrintableInAscii(char c, bool preserveNewLine = false)
    {
        if (IsControlOrNonPrintableWhiteSpace(c, preserveNewLine)) return false;
        if (c >= 0x7F) return false;
        return true;
    }

    private static bool IsPrintableInLatin1(char c, bool preserveNewLine = false)
    {
        if (c >= 0xA0 && c <= 0xFF) return true;
        return IsPrintableInAscii(c, preserveNewLine);
    }

    private static bool IsPrintableAndValidForEncoding(char c, Encoding encoding, bool preserveNewLine = false)
    {
        if (IsControlOrNonPrintableWhiteSpace(c, preserveNewLine)) return false;
        return IsCharValidInEncoding(c, encoding);
    }

    private static bool IsCharValidInEncoding(char c, Encoding encoding)
    {
        // TODO: Optimize this so encoder, encoder.Fallback, and the bytes buffer are out of the hot loop, we can reuse their values.
        var encoder = encoding.GetEncoder();
        encoder.Fallback = new EncoderReplacementFallback("\0");

        // Convert the character to bytes
        byte[] bytes = new byte[encoding.GetMaxByteCount(1)];
        int bytesWritten = encoder.GetBytes([c], 0, 1, bytes, 0, true);

        // Check if the fallback character was used
        return bytesWritten != 0;
    }

    public static bool IsPrintable(char c, Encoding encoding, CharSet charSet, bool preserveNewLine = false)
    {
        return charSet switch
        {
            CharSet.Ascii => IsPrintableInAscii(c, preserveNewLine),
            CharSet.Latin1 => IsPrintableInLatin1(c, preserveNewLine),
            CharSet.CurrentEncoding => IsPrintableAndValidForEncoding(c, encoding, preserveNewLine),
            _ => throw new NotSupportedException($"Unsupported character set: {charSet}")
        };

        // if (char.GetUnicodeCategory(c).)
    }
}