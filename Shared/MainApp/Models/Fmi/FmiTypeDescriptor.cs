using System.Text.RegularExpressions;

namespace FRAMLab.Shared
{
    /// <summary>
    /// Parsed representation of a variable type string from the &lt;FMI&gt; section,
    /// e.g. "number", "bool", "array&lt;number&gt;", "array&lt;array&lt;string&gt;&gt;".
    /// </summary>
    public sealed class FmiTypeDescriptor
    {
        public FmiValueType Kind { get; init; }
        public FmiTypeDescriptor? ElementType { get; init; } //only set for array<...>; list/dictionary are untyped this phase

        public static readonly FmiTypeDescriptor Number = new() { Kind = FmiValueType.Number };
        public static readonly FmiTypeDescriptor Boolean = new() { Kind = FmiValueType.Boolean };
        public static readonly FmiTypeDescriptor String = new() { Kind = FmiValueType.String };

        private static readonly Regex ArrayPattern = new(@"^array\s*<(.+)>$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool TryParse(string typeString, out FmiTypeDescriptor? descriptor, out string? error)
        {
            descriptor = null;
            error = null;
            var trimmed = (typeString ?? "").Trim();
            switch (trimmed.ToLowerInvariant())
            {
                case "number":
                    descriptor = Number;
                    return true;
                case "string":
                    descriptor = String;
                    return true;
                case "bool": //canonical emitted form; "boolean" is accepted as an alias on parse
                case "boolean":
                    descriptor = Boolean;
                    return true;
                case "list":
                    descriptor = new FmiTypeDescriptor { Kind = FmiValueType.List };
                    return true;
                case "dictionary":
                    descriptor = new FmiTypeDescriptor { Kind = FmiValueType.Dictionary };
                    return true;
            }
            var arrayMatch = ArrayPattern.Match(trimmed);
            if (arrayMatch.Success)
            {
                if (!TryParse(arrayMatch.Groups[1].Value, out var element, out error))
                {
                    return false;
                }
                descriptor = new FmiTypeDescriptor { Kind = FmiValueType.Array, ElementType = element };
                return true;
            }
            error = $"Unknown type '{trimmed}'. Expected number, string, bool, list, dictionary or array<...>.";
            return false;
        }

        public override string ToString()
        {
            return Kind switch
            {
                FmiValueType.Number => "number",
                FmiValueType.Boolean => "bool",
                FmiValueType.String => "string",
                FmiValueType.List => "list",
                FmiValueType.Dictionary => "dictionary",
                FmiValueType.Array => $"array<{ElementType}>",
                // Empty is not a declarable type; the fallback keeps serialization total (never throws).
                _ => "string"
            };
        }
    }
}
