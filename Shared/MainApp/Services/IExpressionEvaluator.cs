namespace FRAMLab.Shared.MainApp.Services
{
    /// <summary>Reads api("source", "path") for one function. False (with a reason) when the value is unavailable.</summary>
    public delegate bool FmiApiReader(string source, string path, out string value, out string error);

    public interface IExpressionEvaluator
    {
        string Eval(string expression, Dictionary<string, string[]> variables, List<string[]> dataRows, Dictionary<string, string> dataKeys);

        /// <summary>Same as <see cref="Eval(string, Dictionary{string, string[]}, List{string[]}, Dictionary{string, string})"/>,
        /// additionally recording every variable name that was not found in <paramref name="variables"/>
        /// (it is still read as 0 so parsing can continue; the caller decides what to do).</summary>
        string Eval(string expression, Dictionary<string, string[]> variables, List<string[]> dataRows, Dictionary<string, string> dataKeys, ICollection<string>? unresolved);

        /// <summary>As above, with api("source", "path") read through <paramref name="api"/>
        /// (without a reader api() returns NaN). A failed read evaluates as 0; the reader reports why.</summary>
        string Eval(string expression, Dictionary<string, string[]> variables, List<string[]> dataRows, Dictionary<string, string> dataKeys, ICollection<string>? unresolved, FmiApiReader? api);
    }
}
