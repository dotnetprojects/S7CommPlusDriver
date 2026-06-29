#region License
/******************************************************************************
 * S7CommPlusDriver
 *
 * Copyright (C) 2023 Thomas Wiens, th.wiens@gmx.de
 *
 * This file is part of S7CommPlusDriver.
 *
 * S7CommPlusDriver is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as
 * published by the Free Software Foundation, either version 3 of the
 * License, or (at your option) any later version.
 /****************************************************************************/
#endregion

using System;
using System.Globalization;
using System.Text;

namespace S7CommPlusDriver.Alarming
{
    internal static class S7CommPlusAlarmTextFormatter
    {
        private const int MaxTextListRecursionDepth = 8;

        public static string Format(string text, S7CommPlusAlarmAssociatedValues associatedValues, int languageId)
        {
            return Format(text, associatedValues, languageId, null);
        }

        public static string Format(
            string text,
            S7CommPlusAlarmAssociatedValues associatedValues,
            int languageId,
            Func<string, long, int, string> textListResolver)
        {
            return Format(text, associatedValues, languageId, textListResolver, 0);
        }

        private static string Format(
            string text,
            S7CommPlusAlarmAssociatedValues associatedValues,
            int languageId,
            Func<string, long, int, string> textListResolver,
            int recursionDepth)
        {
            if (String.IsNullOrEmpty(text) || associatedValues == null)
            {
                return text ?? String.Empty;
            }

            var culture = GetCulture(languageId);
            var builder = new StringBuilder(text.Length);
            var copiedUntil = 0;
            var searchFrom = 0;
            while (searchFrom < text.Length)
            {
                var start = text.IndexOf('@', searchFrom);
                if (start < 0)
                {
                    break;
                }

                if (!TryParsePlaceholder(text, start, out var placeholder))
                {
                    searchFrom = start + 1;
                    continue;
                }

                var originalBlock = text.Substring(start, placeholder.End - start);
                var value = associatedValues.GetValue(placeholder.Index);
                var packedStandardValue = 0;
                var hasPackedStandardValue = placeholder.ElementType.HasValue
                    && associatedValues.TryGetPackedStandardInteger(placeholder.Index, placeholder.ElementType.Value, out packedStandardValue);

                if (placeholder.IsTextList)
                {
                    string textListValue;
                    if (hasPackedStandardValue)
                    {
                        textListValue = textListResolver?.Invoke(placeholder.TextListName, packedStandardValue, languageId);
                    }
                    else
                    {
                        if (value == null)
                        {
                            searchFrom = placeholder.End;
                            continue;
                        }

                        textListValue = textListResolver?.Invoke(
                            placeholder.TextListName,
                            value.GetIntegerByElementType(placeholder.ElementType.GetValueOrDefault(GetElementType(value))),
                            languageId);
                    }

                    if (textListValue == null)
                    {
                        searchFrom = placeholder.End;
                        continue;
                    }
                    if (recursionDepth < MaxTextListRecursionDepth)
                    {
                        textListValue = Format(textListValue, associatedValues, languageId, textListResolver, recursionDepth + 1);
                    }

                    builder.Append(text, copiedUntil, start - copiedUntil);
                    builder.Append(textListValue);
                    copiedUntil = placeholder.End;
                    searchFrom = placeholder.End;
                    continue;
                }

                if (value == null && !hasPackedStandardValue)
                {
                    searchFrom = placeholder.End;
                    continue;
                }

                var elementType = placeholder.ElementType.HasValue
                    ? placeholder.ElementType.Value
                    : GetElementType(value);
                var padChar = placeholder.WidthText.StartsWith("0", StringComparison.Ordinal) ? '0' : ' ';
                var width = ParseOptionalInt(placeholder.WidthText).GetValueOrDefault();
                var precision = ParseOptionalInt(placeholder.PrecisionText);
                var replacement = hasPackedStandardValue
                    ? FormatPackedStandardValue(associatedValues, placeholder.Index, packedStandardValue, elementType, placeholder.Format, width, precision, padChar, culture, originalBlock)
                    : FormatValue(value, elementType, placeholder.Format, width, precision, padChar, culture, originalBlock);

                builder.Append(text, copiedUntil, start - copiedUntil);
                builder.Append(replacement);
                copiedUntil = placeholder.End;
                searchFrom = placeholder.End;
            }

            if (copiedUntil == 0)
            {
                return text;
            }

            builder.Append(text, copiedUntil, text.Length - copiedUntil);
            return builder.ToString();
        }

        private static bool TryParsePlaceholder(string text, int start, out AlarmTextPlaceholder placeholder)
        {
            placeholder = default;
            var pos = start + 1;
            if (pos >= text.Length || !Char.IsDigit(text[pos]))
            {
                return false;
            }

            var indexStart = pos;
            while (pos < text.Length && Char.IsDigit(text[pos]))
            {
                pos++;
            }

            if (!Int32.TryParse(text.Substring(indexStart, pos - indexStart), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                return false;
            }

            char? elementType = null;
            if (pos < text.Length && IsElementType(text[pos]))
            {
                elementType = text[pos];
                pos++;
            }

            if (pos >= text.Length || text[pos] != '%')
            {
                return false;
            }
            pos++;

            var widthStart = pos;
            while (pos < text.Length && Char.IsDigit(text[pos]))
            {
                pos++;
            }
            var widthText = text.Substring(widthStart, pos - widthStart);

            var precisionText = String.Empty;
            if (pos < text.Length && text[pos] == '.')
            {
                pos++;
                var precisionStart = pos;
                while (pos < text.Length && Char.IsDigit(text[pos]))
                {
                    pos++;
                }
                precisionText = text.Substring(precisionStart, pos - precisionStart);
            }

            if (pos >= text.Length)
            {
                return false;
            }

            if (text[pos] == 't' && pos + 1 < text.Length && text[pos + 1] == '#')
            {
                var end = text.IndexOf('@', pos + 2);
                if (end < 0)
                {
                    return false;
                }

                var textListName = text.Substring(pos + 2, end - pos - 2);
                placeholder = new AlarmTextPlaceholder(index, elementType, widthText, precisionText, '\0', end + 1, true, textListName);
                return true;
            }

            if (!IsDisplayFormat(text[pos]))
            {
                return false;
            }

            var format = text[pos];
            pos++;
            if (pos >= text.Length || text[pos] != '@')
            {
                return false;
            }

            placeholder = new AlarmTextPlaceholder(index, elementType, widthText, precisionText, format, pos + 1, false, String.Empty);
            return true;
        }

        private static bool IsElementType(char value)
        {
            switch (value)
            {
                case 'B':
                case 'C':
                case 'D':
                case 'I':
                case 'O':
                case 'R':
                case 'T':
                case 'W':
                case 'X':
                case 'Y':
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsDisplayFormat(char value)
        {
            switch (value)
            {
                case 'b':
                case 'd':
                case 'f':
                case 's':
                case 'u':
                case 'X':
                case 'x':
                    return true;
                default:
                    return false;
            }
        }

        private readonly struct AlarmTextPlaceholder
        {
            public AlarmTextPlaceholder(
                int index,
                char? elementType,
                string widthText,
                string precisionText,
                char format,
                int end,
                bool isTextList,
                string textListName)
            {
                Index = index;
                ElementType = elementType;
                WidthText = widthText;
                PrecisionText = precisionText;
                Format = format;
                End = end;
                IsTextList = isTextList;
                TextListName = textListName;
            }

            public int Index { get; }
            public char? ElementType { get; }
            public string WidthText { get; }
            public string PrecisionText { get; }
            public char Format { get; }
            public int End { get; }
            public bool IsTextList { get; }
            public string TextListName { get; }
        }

        private static string FormatValue(
            S7CommPlusAlarmAssociatedValue value,
            char elementType,
            char format,
            int width,
            int? precision,
            char padChar,
            CultureInfo culture,
            string originalBlock)
        {
            switch (format)
            {
                case 'd':
                    return FormatDecimalSigned(MakeSigned(value.GetIntegerByElementType(elementType), elementType), width, padChar);
                case 'u':
                    return FormatDecimalUnsigned((uint)value.GetIntegerByElementType(elementType), width, padChar);
                case 'x':
                case 'X':
                    return FormatHex(value.GetIntegerByElementType(elementType), width);
                case 'b':
                    return FormatBinary(value.GetIntegerByElementType(elementType), width);
                case 'f':
                    if (elementType != 'R' && elementType != 'X' && elementType != 'D' && elementType != 'O')
                    {
                        return originalBlock;
                    }
                    return FormatFloatingPoint(value.GetRealByElementType(elementType), precision, width, padChar, culture);
                case 's':
                    if (value.IsString)
                    {
                        return ApplyMinimumLength(value.GetString(), width, padChar);
                    }
                    return FormatAsString(value.GetIntegerByElementType(elementType), width, padChar);
                default:
                    return originalBlock;
            }
        }

        private static string FormatPackedStandardValue(
            S7CommPlusAlarmAssociatedValues associatedValues,
            int position,
            int value,
            char elementType,
            char format,
            int width,
            int? precision,
            char padChar,
            CultureInfo culture,
            string originalBlock)
        {
            switch (format)
            {
                case 'd':
                    return FormatDecimalSigned(MakeSigned(value, elementType), width, padChar);
                case 'u':
                    return FormatDecimalUnsigned((uint)value, width, padChar);
                case 'x':
                case 'X':
                    return FormatHex(value, width);
                case 'b':
                    return FormatBinary(value, width);
                case 'f':
                    if (elementType != 'R' && elementType != 'X' && elementType != 'D' && elementType != 'O')
                    {
                        return originalBlock;
                    }

                    if (!associatedValues.TryGetPackedStandardReal(position, elementType, out var realValue))
                    {
                        return originalBlock;
                    }

                    return FormatFloatingPoint(realValue, precision, width, padChar, culture);
                case 's':
                    return FormatAsString(value, width, padChar);
                default:
                    return originalBlock;
            }
        }

        private static string FormatBinary(int value, int width)
        {
            var text = MakeBitString(value).PadLeft(width, '0');
            return "2#" + text;
        }

        private static string FormatDecimalSigned(int value, int width, char padChar)
        {
            string text;
            if (value >= 0 || padChar == ' ')
            {
                text = value.ToString("D", CultureInfo.CurrentCulture);
                return text.PadLeft(width, padChar);
            }

            value *= -1;
            text = value.ToString("D", CultureInfo.CurrentCulture);
            if (width > 0)
            {
                width--;
            }

            return "-" + text.PadLeft(width, padChar);
        }

        private static string FormatDecimalUnsigned(uint value, int width, char padChar)
        {
            return value.ToString("D", CultureInfo.CurrentCulture).PadLeft(width, padChar);
        }

        private static string FormatFloatingPoint(double value, int? precision, int width, char padChar, CultureInfo culture)
        {
            var decimals = precision.GetValueOrDefault(6);
            var text = width <= 0 ? GetENumber(value, decimals, 12, culture) : GetENumber(value, decimals, width, culture);
            if (width <= 0)
            {
                return text;
            }

            if (value >= 0.0 || padChar == ' ')
            {
                return text.PadLeft(width, padChar);
            }

            text = text.Substring(1);
            width--;
            return "-" + text.PadLeft(width, padChar);
        }

        private static string FormatHex(int value, int width)
        {
            var text = value.ToString("X", CultureInfo.CurrentCulture).PadLeft(width, '0');
            return "16#" + text;
        }

        private static string FormatAsString(int value, int width, char padChar)
        {
            return GetStringOfValue(value).PadLeft(width, padChar);
        }

        private static string ApplyMinimumLength(string value, int length, char padChar)
        {
            if (length <= value.Length)
            {
                return value;
            }

            return value.PadLeft(length, padChar);
        }

        private static string GetENumber(double number, int decimals, int positiveNumbers, CultureInfo culture)
        {
            if (number > 1.0)
            {
                if (number > Math.Pow(10.0, positiveNumbers))
                {
                    return number.ToString("e" + decimals.ToString(CultureInfo.InvariantCulture), culture);
                }
                return Math.Round(number, decimals, MidpointRounding.AwayFromZero).ToString(culture);
            }

            if (number > -1.0)
            {
                if (Math.Round(number, decimals, MidpointRounding.AwayFromZero) > 0.0)
                {
                    return Math.Round(number, decimals, MidpointRounding.AwayFromZero).ToString(culture);
                }
                if (number == 0.0)
                {
                    return number.ToString("f1", culture);
                }
                return number.ToString("e" + decimals.ToString(CultureInfo.InvariantCulture), culture);
            }

            if (number < Math.Pow(10.0, positiveNumbers) * -1.0)
            {
                return number.ToString("e" + decimals.ToString(CultureInfo.InvariantCulture), culture);
            }

            return Math.Round(number, decimals, MidpointRounding.AwayFromZero).ToString(culture);
        }

        private static char GetElementType(S7CommPlusAlarmAssociatedValue value)
        {
            switch (value.TypeInfo)
            {
                case Ids.TI_BOOL:
                    return 'B';
                case Ids.TI_BYTE:
                case Ids.TI_CHAR:
                case Ids.TI_USINT:
                case Ids.TI_SINT:
                    return 'Y';
                case Ids.TI_WORD:
                case Ids.TI_UINT:
                case Ids.TI_WCHAR:
                    return 'W';
                case Ids.TI_INT:
                    return 'I';
                case Ids.TI_DWORD:
                case Ids.TI_UDINT:
                    return 'X';
                case Ids.TI_DINT:
                    return 'D';
                case Ids.TI_REAL:
                    return 'R';
                case Ids.TI_LREAL:
                    return 'O';
                case Ids.TI_STRING:
                case Ids.TI_WSTRING:
                    return 'T';
                default:
                    if (value.TypeInfo > Ids.TI_STRING_START && value.TypeInfo <= Ids.TI_WSTRING_END)
                    {
                        return 'T';
                    }
                    return 'X';
            }
        }

        private static string GetStringOfValue(int value)
        {
            char[] chars = new char[4] { ' ', ' ', ' ', ' ' };
            char[] raw = new char[4];
            raw[3] = (char)(value & 0xFF);
            raw[2] = (char)((value & 0xFF00) >> 8);
            raw[1] = (char)((value & 0xFF0000) >> 16);
            raw[0] = (char)((value & 0xFF000000u) >> 24);
            for (int i = 3; i > -1; i--)
            {
                if (raw[i] >= ' ' && (raw[i] <= '~' || raw[i] >= '\u00a0'))
                {
                    chars[i] = raw[i];
                }
            }

            return new string(chars).Trim();
        }

        private static int MakeSigned(int value, char elementType)
        {
            switch (GetElementTypeLength(elementType))
            {
                case 1:
                    if ((value & 0x80) != 0)
                    {
                        return value | -256;
                    }
                    break;
                case 2:
                    if ((value & 0x8000) != 0)
                    {
                        return value | -65536;
                    }
                    break;
                case 3:
                    if ((value & 0x800000) != 0)
                    {
                        return value | -16777216;
                    }
                    break;
            }

            return value;
        }

        private static int GetElementTypeLength(char elementType)
        {
            switch (elementType)
            {
                case 'B':
                case 'Y':
                case 'C':
                    return 1;
                case 'W':
                case 'I':
                    return 2;
                case 'X':
                case 'D':
                case 'R':
                    return 4;
                case 'O':
                    return 8;
                default:
                    return 0;
            }
        }

        private static string MakeBitString(int value)
        {
            var builder = new StringBuilder();
            var hasOne = false;
            uint mask = 0x80000000;
            for (var i = 0; i < 32; i++)
            {
                if ((value & mask) == 0)
                {
                    if (hasOne)
                    {
                        builder.Append('0');
                    }
                }
                else
                {
                    builder.Append('1');
                    hasOne = true;
                }
                mask >>= 1;
            }

            if (builder.Length == 0)
            {
                builder.Append('0');
            }

            return builder.ToString();
        }

        private static int? ParseOptionalInt(string value)
        {
            if (String.IsNullOrEmpty(value))
            {
                return null;
            }

            if (Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            return null;
        }

        private static CultureInfo GetCulture(int languageId)
        {
            try
            {
                return CultureInfo.GetCultureInfo(languageId);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }
    }
}
