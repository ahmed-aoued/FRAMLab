using System;
using System.Globalization;

namespace FRAMLab.Shared.MainApp.Services.Scripting
{
    /// <summary>Runtime value types for the interpreter.</summary>
    public enum STValueType { Bool, Int, Real, String, Time, Void }

    /// <summary>A typed runtime value. Numbers are kept as long/double and promoted on demand.</summary>
    public readonly struct STValue
    {
        public STValueType Type { get; }
        private readonly object? raw;

        private STValue(STValueType type, object? raw) { Type = type; this.raw = raw; }

        public static STValue FromBool(bool b) => new(STValueType.Bool, b);
        public static STValue FromInt(long i) => new(STValueType.Int, i);
        public static STValue FromReal(double d) => new(STValueType.Real, d);
        public static STValue FromString(string s) => new(STValueType.String, s);
        public static STValue FromTime(TimeSpan t) => new(STValueType.Time, t);
        public static STValue Void => new(STValueType.Void, null);

        public bool AsBool() => Type switch
        {
            STValueType.Bool => (bool)raw!,
            STValueType.Int => (long)raw! != 0,
            STValueType.Real => (double)raw! != 0.0,
            _ => false,
        };
        public long AsInt() => Type switch
        {
            STValueType.Int => (long)raw!,
            STValueType.Real => (long)(double)raw!,
            STValueType.Bool => (bool)raw! ? 1 : 0,
            _ => 0,
        };
        public double AsReal() => Type switch
        {
            STValueType.Real => (double)raw!,
            STValueType.Int => (long)raw!,
            STValueType.Bool => (bool)raw! ? 1.0 : 0.0,
            STValueType.Time => ((TimeSpan)raw!).TotalMilliseconds,
            _ => 0.0,
        };
        public string AsString() => Type switch
        {
            STValueType.String => (string)raw!,
            STValueType.Bool => (bool)raw! ? "TRUE" : "FALSE",
            STValueType.Int => ((long)raw!).ToString(CultureInfo.InvariantCulture),
            STValueType.Real => ((double)raw!).ToString(CultureInfo.InvariantCulture),
            STValueType.Time => $"T#{((TimeSpan)raw!).TotalMilliseconds}ms",
            _ => "",
        };
        public TimeSpan AsTime() => Type == STValueType.Time ? (TimeSpan)raw! : TimeSpan.FromMilliseconds(AsReal());

        public bool IsNumeric => Type is STValueType.Int or STValueType.Real;
        public override string ToString() => AsString();
    }
}
