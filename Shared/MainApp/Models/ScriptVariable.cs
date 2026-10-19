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
        public ScriptVarType Type { get; set; } = ScriptVarType.REAL;
        public string InitialValue { get; set; } = "";
        /// <summary>When true, the value is written back to the function metadata after each cycle (script output).</summary>
        public bool IsOutput { get; set; } = false;

        public ScriptVariable Clone() => new ScriptVariable
        {
            Name = Name,
            Scope = Scope,
            Type = Type,
            InitialValue = InitialValue,
            IsOutput = IsOutput
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

        /// <summary>Basic syntactic validation of an initial-value literal for the given type.</summary>
        public static bool IsValidInit(ScriptVarType type, string init)
        {
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
    }
}
