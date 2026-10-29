namespace FRAMLab.Shared.MainApp.Models
{
    /// <summary>
    /// Lifetime/visibility of a script variable.
    /// Temp   - re-initialised every FMI cycle, visible only to the owning function.
    /// Static - kept for a whole FMI run, visible only to the owning function.
    /// Global - kept for a whole FMI run, accessible from all FRAM functions.
    /// </summary>
    public enum ScriptScope
    {
        Temp,
        Static,
        Global
    }

    /// <summary>
    /// IEC 61131-3 elementary data types supported by the ST script editor.
    /// </summary>
    public enum ScriptVarType
    {
        BOOL,
        INT,
        REAL,
        STRING,
        TIME
    }

    /// <summary>
    /// One row of the Variable Declaration table of a function script.
    /// Persisted as a &lt;var&gt; element inside the function's &lt;variables&gt; node.
    /// </summary>
    public class ScriptVariable
    {
        public string Name { get; set; } = "";
        public ScriptScope Scope { get; set; } = ScriptScope.Temp;
        /// <summary>Element type for scalar variables and arrays alike.</summary>
        public ScriptVarType Type { get; set; } = ScriptVarType.REAL;
        public string InitialValue { get; set; } = "";
        /// <summary>When true, the value is written back to the function metadata after each cycle (script output).</summary>
        public bool IsOutput { get; set; } = false;

        /// <summary>When true this variable is a one-dimensional array of Type. Arrays start at index 0
        /// and grow when an element is written; there is no declared size.</summary>
        public bool IsArray { get; set; } = false;
        /// <summary>Legacy bounds from older files (lower/upper attributes). Only used to place a single
        /// initial value at positions lower..upper so existing scripts read the same elements.</summary>
        public int LowerBound { get; set; } = 0;
        public int UpperBound { get; set; } = 0;
        public bool HasLegacyBounds => IsArray && (LowerBound != 0 || UpperBound != 0);
        /// <summary>For arrays: 1 = list (a[i]), 2 = table (m[row, col]).</summary>
        public int Dimensions { get; set; } = 1;

        /// <summary>Human-readable shape, e.g. "2-D ARRAY OF REAL".</summary>
        public bool IsTable => IsArray && Dimensions == 2;

        /// <summary>False when the user left the initial value empty; such a variable
        /// must be assigned in the script before it is read.</summary>
        public bool HasInitialValue => !string.IsNullOrWhiteSpace(InitialValue);

        /// <summary>Human-readable declaration used in messages, e.g. ARRAY OF REAL.</summary>
        public string TypeName => IsArray ? $"{(IsTable ? "2-D " : "")}ARRAY OF {Type}" : Type.ToString();

        public ScriptVariable Clone() => new ScriptVariable
        {
            Name = Name,
            Scope = Scope,
            Type = Type,
            InitialValue = InitialValue,
            IsOutput = IsOutput,
            IsArray = IsArray,
            Dimensions = Dimensions,
            LowerBound = LowerBound,
            UpperBound = UpperBound
        };

        public static string DefaultInit(ScriptVarType type) => type switch
        {
            ScriptVarType.BOOL => "FALSE",
            ScriptVarType.INT => "0",
            ScriptVarType.REAL => "0.0",
            ScriptVarType.STRING => "",
            ScriptVarType.TIME => "T#0s",
            _ => "0"
        };

        /// <summary>Basic syntactic validation of an initial-value literal for the given type.
        /// An empty value is allowed: it means "not initialized at declaration time".</summary>
        public static bool IsValidInit(ScriptVarType type, string init)
        {
            if (string.IsNullOrWhiteSpace(init)) return true;
            switch (type)
            {
                case ScriptVarType.BOOL:
                    return init is "TRUE" or "FALSE" or "true" or "false" or "0" or "1";
                case ScriptVarType.INT:
                    return long.TryParse(init, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out _);
                case ScriptVarType.REAL:
                    return double.TryParse(init, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _);
                case ScriptVarType.STRING:
                    return true; // anything goes
                case ScriptVarType.TIME:
                    return System.Text.RegularExpressions.Regex.IsMatch(init,
                        @"^(T|TIME)?#-?\d+(\.\d+)?(ms|s|m|h|d)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Validates an array initial value: either a single scalar (applied to every
        /// element) or a comma-separated / bracketed list of element literals.
        /// </summary>
        /// <summary>Validates the initial value of any declaration (scalar, list or table).</summary>
        public static bool IsValidInitFor(ScriptVariable v)
            => !v.IsArray ? IsValidInit(v.Type, v.InitialValue)
             : v.IsTable ? IsValidTableInit(v.Type, v.InitialValue)
             : IsValidArrayInit(v.Type, v.InitialValue);

        /// <summary>A table initial value: rows separated by ';' ("1,2;3,4") or nested brackets
        /// ("[[1,2],[3,4]]"). Rows may differ in length; short rows are padded with zeros.</summary>
        public static List<List<string>> SplitTableInit(string init)
        {
            var text = (init ?? "").Trim();
            if (text.Length == 0) return new List<List<string>>();
            if (text.StartsWith("[[") && text.EndsWith("]]"))
            {
                text = text.Substring(1, text.Length - 2);
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\]\s*,\s*\[", ";");
                text = text.Trim().TrimStart('[').TrimEnd(']');
            }
            return text.Split(';').Select(row => SplitArrayInit(row)).ToList();
        }

        public static bool IsValidTableInit(ScriptVarType type, string init)
        {
            foreach (var row in SplitTableInit(init))
            {
                foreach (var cell in row)
                {
                    if (!IsValidInit(type, cell)) return false;
                }
            }
            return true;
        }

        public static bool IsValidArrayInit(ScriptVarType type, string init)
        {
            var parts = SplitArrayInit(init);
            if (parts.Count == 0) return true; // empty: the array starts with no elements
            foreach (var part in parts)
            {
                if (!IsValidInit(type, part)) return false;
            }
            return true;
        }

        /// <summary>Split '1,2,3' or '[1, 2, 3]' into element literals, respecting quoted strings.</summary>
        public static List<string> SplitArrayInit(string init)
        {
            var result = new List<string>();
            var text = (init ?? "").Trim();
            if (text.Length == 0) return result;
            if (text.StartsWith("[") && text.EndsWith("]") && text.Length >= 2)
            {
                text = text.Substring(1, text.Length - 2);
            }

            var parts = new List<string>();
            var current = new System.Text.StringBuilder();
            char quote = '\0';
            foreach (var c in text)
            {
                if (quote != '\0')
                {
                    current.Append(c);
                    if (c == quote) quote = '\0';
                }
                else if (c == '\'' || c == '"')
                {
                    quote = c;
                    current.Append(c);
                }
                else if (c == ',')
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            parts.Add(current.ToString());

            foreach (var p in parts)
            {
                var value = p.Trim();
                if (value.Length > 0) result.Add(value);
            }
            return result;
        }
    }
}
