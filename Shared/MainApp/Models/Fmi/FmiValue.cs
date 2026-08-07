using System.Globalization;
using System.Linq;

namespace FRAMLab.Shared
{
    public enum FmiValueType { Empty, Number, Boolean, String, Array, List, Dictionary } //Table reserved for a later phase

    /// <summary>
    /// Typed runtime value for the FMI runtime. Numbers and booleans are stored inline
    /// (no allocation); strings, arrays, lists and dictionaries share a single reference field.
    /// ToLegacyString/FromLegacyString bridge to the string-based ExpressionEvaluator world.
    /// </summary>
    public readonly struct FmiValue
    {
        public FmiValueType Type { get; }
        private readonly double _number; //Number value, or 0/1 for Boolean
        private readonly object? _ref;   //string | List<FmiValue> | Dictionary<string, FmiValue>

        public static readonly FmiValue Empty = default;

        private FmiValue(FmiValueType type, double number, object? reference)
        {
            Type = type;
            _number = number;
            _ref = reference;
        }

        public static FmiValue Number(double value) => new(FmiValueType.Number, value, null);
        public static FmiValue Bool(bool value) => new(FmiValueType.Boolean, value ? 1 : 0, null);
        public static FmiValue Str(string value) => new(FmiValueType.String, 0, value ?? "");
        public static FmiValue Array(List<FmiValue> items) => new(FmiValueType.Array, 0, items ?? new List<FmiValue>());
        public static FmiValue List(List<FmiValue> items) => new(FmiValueType.List, 0, items ?? new List<FmiValue>());
        public static FmiValue Dict(Dictionary<string, FmiValue> entries) => new(FmiValueType.Dictionary, 0, entries ?? new Dictionary<string, FmiValue>());

        public double AsNumber()
        {
            switch (Type)
            {
                case FmiValueType.Number:
                case FmiValueType.Boolean:
                    return _number;
                case FmiValueType.String:
                    return double.TryParse((string)_ref!, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN;
                default:
                    return double.NaN;
            }
        }

        public bool AsBool()
        {
            switch (Type)
            {
                case FmiValueType.Boolean:
                case FmiValueType.Number:
                    return _number != 0;
                case FmiValueType.String:
                    var s = (string)_ref!;
                    return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase); //matches the engine's "1"/"true" checks
                default:
                    return false;
            }
        }

        public string? AsString() => Type == FmiValueType.String ? (string)_ref! : null;

        public List<FmiValue>? AsList() => Type is FmiValueType.Array or FmiValueType.List ? (List<FmiValue>)_ref! : null;

        public Dictionary<string, FmiValue>? AsDictionary() => Type == FmiValueType.Dictionary ? (Dictionary<string, FmiValue>)_ref! : null;

        /// <summary>
        /// Renders the value in the string convention the legacy engine uses:
        /// numbers invariant, booleans "1"/"0", arrays/lists comma-joined (Function.array() convention).
        /// </summary>
        public string ToLegacyString()
        {
            return Type switch
            {
                FmiValueType.Number => _number.ToString(CultureInfo.InvariantCulture),
                FmiValueType.Boolean => _number != 0 ? "1" : "0",
                FmiValueType.String => (string)_ref!,
                FmiValueType.Array or FmiValueType.List => string.Join(",", ((List<FmiValue>)_ref!).Select(v => v.ToLegacyString())),
                FmiValueType.Dictionary => string.Join(",", ((Dictionary<string, FmiValue>)_ref!).Select(kv => $"{kv.Key}:{kv.Value.ToLegacyString()}")),
                _ => ""
            };
        }

        /// <summary>
        /// Parses a legacy string value. Without a declared type the kind is inferred
        /// (number, then bool, else string). With a declared type the raw value is coerced;
        /// when coercion fails the raw string is returned and <paramref name="coerced"/> is false
        /// so the caller can emit a diagnostic.
        /// </summary>
        public static FmiValue FromLegacyString(string raw, FmiTypeDescriptor? declared = null) => FromLegacyString(raw, declared, out _);

        public static FmiValue FromLegacyString(string raw, FmiTypeDescriptor? declared, out bool coerced)
        {
            coerced = true;
            raw ??= "";
            if (declared is null)
            {
                if (raw == "") return Empty;
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var inferred)) return Number(inferred);
                if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return Bool(true);
                if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return Bool(false);
                return Str(raw);
            }
            switch (declared.Kind)
            {
                case FmiValueType.Number:
                    if (raw == "") return Number(0);
                    if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) return Number(num);
                    coerced = false;
                    return Str(raw);
                case FmiValueType.Boolean:
                    if (raw == "" || raw == "0" || raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return Bool(false);
                    if (raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return Bool(true);
                    coerced = false;
                    return Str(raw);
                case FmiValueType.String:
                    return Str(raw);
                case FmiValueType.Array:
                case FmiValueType.List:
                    var items = new List<FmiValue>();
                    if (raw != "")
                    {
                        foreach (var part in raw.Split(','))
                        {
                            items.Add(FromLegacyString(part.Trim(), declared.ElementType, out var itemOk));
                            coerced &= itemOk;
                        }
                    }
                    return declared.Kind == FmiValueType.Array ? Array(items) : List(items);
                case FmiValueType.Dictionary:
                    var entries = new Dictionary<string, FmiValue>();
                    if (raw != "")
                    {
                        foreach (var part in raw.Split(','))
                        {
                            var split = part.Split(':', 2);
                            if (split.Length == 2)
                            {
                                entries[split[0].Trim()] = FromLegacyString(split[1].Trim(), null, out _);
                            }
                            else
                            {
                                coerced = false;
                            }
                        }
                    }
                    return Dict(entries);
                default:
                    return raw == "" ? Empty : Str(raw);
            }
        }
    }
}
