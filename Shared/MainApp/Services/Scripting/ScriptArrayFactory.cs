using System;
using System.Collections.Generic;
using System.Globalization;
using FRAMLab.Shared.MainApp.Models;

namespace FRAMLab.Shared.MainApp.Services.Scripting
{
    /// <summary>
    /// Builds and coerces runtime array values. Shared by the interpreter (Temp arrays)
    /// and the scripting service (Static/Global arrays).
    /// </summary>
    public static class ScriptArrayFactory
    {
        /// <summary>Safety cap so a bad declaration cannot allocate unbounded memory.</summary>
        public const int MaxLength = STArray.MaxLength;

        /// <summary>
        /// Create the initial value described by a declaration. A list ("1,2,3" or "[1,2,3]")
        /// becomes elements 0..n-1. A single value is one element, unless an older file declared
        /// bounds (lower/upper): then positions lower..upper hold the value, so existing scripts
        /// that index from 1 keep reading the same elements. No initial value: an empty array.
        /// </summary>
        public static STArray Create(ScriptVariable v)
        {
            if (v.IsTable)
            {
                // rows of cells; the table is padded to the widest row
                var rows = ScriptVariable.SplitTableInit(v.InitialValue)
                    .Where(r => r.Count > 0)
                    .Select(r => r.Select(cell => TryParse(v.Type, cell)));
                var table = STArray.FromRows(v.Type, rows);
                return table.Length * table.Width <= MaxLength ? table : new STArray(v.Type, rank: 2);
            }
            var parts = ScriptVariable.SplitArrayInit(v.InitialValue);
            if (parts.Count == 0)
            {
                return new STArray(v.Type);
            }
            if (parts.Count > 1)
            {
                var count = Math.Min(parts.Count, MaxLength);
                return new STArray(v.Type, ParseElements(v.Type, count, v.InitialValue));
            }

            var value = TryParse(v.Type, parts[0]);
            var array = new STArray(v.Type);
            var lower = Math.Max(0, v.LowerBound);
            var upper = Math.Min(Math.Max(v.UpperBound, lower), MaxLength - 1);
            for (var i = lower; i <= upper; i++)
            {
                array.Set(i, value);
            }
            return array;
        }

        /// <summary>
        /// Parse an initial value into <paramref name="length"/> typed elements.
        /// A single literal fills the whole array; a comma-separated list is applied
        /// element by element (missing entries become typed zeros).
        /// </summary>
        public static STValue[] ParseElements(ScriptVarType type, int length, string init)
        {
            if (length <= 0) length = 1;
            var values = new STValue[length];
            var parts = ScriptVariable.SplitArrayInit(init);

            if (parts.Count == 0)
            {
                for (var i = 0; i < length; i++) values[i] = ZeroOf(type);
                return values;
            }

            if (parts.Count == 1)
            {
                var value = TryParse(type, parts[0]);
                for (var i = 0; i < length; i++) values[i] = value;
                return values;
            }

            for (var i = 0; i < length; i++)
            {
                values[i] = i < parts.Count ? TryParse(type, parts[i]) : ZeroOf(type);
            }
            return values;
        }

        public static STValue TryParse(ScriptVarType type, string raw)
        {
            var text = (raw ?? "").Trim();
            if (text.Length >= 2
                && ((text[0] == '\'' && text[^1] == '\'') || (text[0] == '"' && text[^1] == '"')))
            {
                text = text.Substring(1, text.Length - 2);
            }

            switch (type)
            {
                case ScriptVarType.BOOL:
                    return STValue.FromBool(text.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                                            || text.Equals("true", StringComparison.OrdinalIgnoreCase)
                                            || text == "1");
                case ScriptVarType.INT:
                    return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                        ? STValue.FromInt(l)
                        : STValue.FromInt(0);
                case ScriptVarType.REAL:
                    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                        ? STValue.FromReal(d)
                        : STValue.FromReal(0);
                case ScriptVarType.STRING:
                    return STValue.FromString(text);
                case ScriptVarType.TIME:
                    return STValue.FromTime(ParseTime(text));
                default:
                    return STValue.FromReal(0);
            }
        }

        /// <summary>Convert a value to the declared element type (used for element writes).</summary>
        public static STValue Coerce(STValue value, ScriptVarType type)
        {
            if (value.Type == STValueType.Void) return ZeroOf(type);
            return type switch
            {
                ScriptVarType.BOOL => STValue.FromBool(value.AsBool()),
                ScriptVarType.INT => STValue.FromInt(value.AsInt()),
                ScriptVarType.REAL => STValue.FromReal(value.AsReal()),
                ScriptVarType.STRING => STValue.FromString(value.AsString()),
                ScriptVarType.TIME => STValue.FromTime(value.AsTime()),
                _ => value,
            };
        }

        public static STValue ZeroOf(ScriptVarType type) => type switch
        {
            ScriptVarType.BOOL => STValue.FromBool(false),
            ScriptVarType.INT => STValue.FromInt(0),
            ScriptVarType.REAL => STValue.FromReal(0),
            ScriptVarType.STRING => STValue.FromString(""),
            ScriptVarType.TIME => STValue.FromTime(TimeSpan.Zero),
            _ => STValue.FromReal(0),
        };

        /// <summary>Infer the element type of an array literal; mixed numerics promote to REAL.</summary>
        public static ScriptVarType InferElementType(IReadOnlyList<STValue> values)
        {
            if (values.Count == 0) return ScriptVarType.REAL;
            var hasReal = false;
            var hasInt = false;
            var hasOther = false;
            ScriptVarType other = ScriptVarType.REAL;

            foreach (var v in values)
            {
                switch (v.Type)
                {
                    case STValueType.Int: hasInt = true; break;
                    case STValueType.Real: hasReal = true; break;
                    case STValueType.Bool:
                        if (!hasOther) { hasOther = true; other = ScriptVarType.BOOL; }
                        else if (other != ScriptVarType.BOOL) hasReal = true;
                        break;
                    case STValueType.String:
                        if (!hasOther) { hasOther = true; other = ScriptVarType.STRING; }
                        else if (other != ScriptVarType.STRING) hasReal = true;
                        break;
                    case STValueType.Time:
                        if (!hasOther) { hasOther = true; other = ScriptVarType.TIME; }
                        else if (other != ScriptVarType.TIME) hasReal = true;
                        break;
                    default:
                        hasReal = true;
                        break;
                }
            }

            if (hasReal) return ScriptVarType.REAL;
            if (hasInt) return ScriptVarType.INT;
            return other;
        }

        /// <summary>Parse a TIME value such as <c>T#5s</c>, <c>TIME#100ms</c> or <c>#2m</c>.</summary>
        public static TimeSpan ParseTime(string text)
        {
            if (string.IsNullOrEmpty(text)) return TimeSpan.Zero;
            var hash = text.IndexOf('#');
            if (hash >= 0) text = text.Substring(hash + 1);
            var negative = text.StartsWith("-");
            if (negative) text = text.Substring(1);

            var i = 0;
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
            var number = text.Substring(0, i);
            var unit = text.Substring(i).ToLowerInvariant();
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            {
                amount = 0;
            }
            var ms = unit switch
            {
                "ms" => amount,
                "s" or "" => amount * 1000,
                "m" => amount * 60_000,
                "h" => amount * 3_600_000,
                "d" => amount * 86_400_000,
                _ => amount * 1000,
            };
            var span = TimeSpan.FromMilliseconds(ms);
            return negative ? -span : span;
        }
    }
}
