using System.Collections.Generic;
using System.Linq;

namespace FRAMLab.Shared.MainApp.Services.Scripting
{
    /// <summary>One autocomplete suggestion for the script editor (serialized to Monaco).</summary>
    public sealed record ScriptCompletionItem(
        string Label,
        string Kind,          // variable | aspect | function | keyword | snippet
        string Detail,        // short text next to the label, e.g. "Static INT"
        string Documentation,
        string InsertText,
        bool IsSnippet);

    /// <summary>
    /// Builds the editor's autocomplete list from the same symbol table the compiler uses, so
    /// suggestions always match what compiles: declared variables, aspect variables, the host
    /// interface and maths built-ins (with parameter placeholders), keywords and statement snippets.
    /// </summary>
    public static class ScriptCompletions
    {
        private static readonly (string Name, string Insert, string Doc)[] HostFunctions =
        {
            ("GetInput", "GetInput('${1:key}')", "Value of an Input aspect, by key or aspect label."),
            ("GetPrecondition", "GetPrecondition('${1:key}')", "Value of a Precondition aspect."),
            ("GetResource", "GetResource('${1:key}')", "Value of a Resource aspect."),
            ("GetControl", "GetControl('${1:key}')", "Value of a Control aspect."),
            ("GetTime", "GetTime('${1:key}')", "Value of a Time aspect."),
            ("SetOutput", "SetOutput('${1:key}', ${2:value})", "Write a value to an output key; it flows downstream along the couplings."),
            ("IsActive", "IsActive()", "TRUE when the function's activation profile is met this cycle."),
            ("API", "API('${1:source}', '${2:path}')", "Live data value from a data source (FMI Setup → Data sources), e.g. API('weather', 'current.temperature_2m')."),
            ("Print", "Print(${1:value})", "Write the arguments, joined, to the FMI log."),
            ("Trace", "Trace(${1:value})", "Same as Print."),
        };

        private static readonly (string Name, string Insert, string Doc)[] MathFunctions =
        {
            ("ABS", "ABS(${1:x})", "Absolute value."),
            ("SQRT", "SQRT(${1:x})", "Square root."),
            ("SIN", "SIN(${1:x})", "Sine (radians)."),
            ("COS", "COS(${1:x})", "Cosine (radians)."),
            ("TAN", "TAN(${1:x})", "Tangent (radians)."),
            ("LN", "LN(${1:x})", "Natural logarithm."),
            ("EXP", "EXP(${1:x})", "e to the power x."),
            ("MIN", "MIN(${1:a}, ${2:b})", "Smallest of the arguments (any number)."),
            ("MAX", "MAX(${1:a}, ${2:b})", "Largest of the arguments (any number)."),
            ("LIMIT", "LIMIT(${1:min}, ${2:x}, ${3:max})", "x clamped to the range min..max."),
            ("ROUND", "ROUND(${1:x})", "Nearest whole number (INT)."),
            ("TRUNC", "TRUNC(${1:x})", "Whole-number part (INT)."),
        };

        private static readonly string[] Keywords =
        {
            "IF", "THEN", "ELSIF", "ELSE", "END_IF", "FOR", "TO", "BY", "DO", "END_FOR",
            "WHILE", "END_WHILE", "REPEAT", "UNTIL", "END_REPEAT", "CASE", "OF", "END_CASE",
            "RETURN", "EXIT", "AND", "OR", "XOR", "NOT", "MOD", "TRUE", "FALSE",
        };

        private static readonly (string Label, string Insert, string Doc)[] Snippets =
        {
            ("IF … END_IF", "IF ${1:condition} THEN\n\t$0\nEND_IF;", "Conditional block."),
            ("IF … ELSE … END_IF", "IF ${1:condition} THEN\n\t$2\nELSE\n\t$0\nEND_IF;", "Conditional with an ELSE branch."),
            ("FOR … END_FOR", "FOR ${1:i} := ${2:0} TO ${3:9} DO\n\t$0\nEND_FOR;", "Counting loop."),
            ("WHILE … END_WHILE", "WHILE ${1:condition} DO\n\t$0\nEND_WHILE;", "Loop while a condition holds."),
            ("REPEAT … END_REPEAT", "REPEAT\n\t$0\nUNTIL ${1:condition}\nEND_REPEAT;", "Loop that runs at least once."),
            ("CASE … END_CASE", "CASE ${1:value} OF\n\t${2:1}: $3\n\t${4:2, 3}: $5\nELSE\n\t$0\nEND_CASE;", "Branch on a whole number: labels, lists (2, 3) and ranges (4..9)."),
        };

        public static List<ScriptCompletionItem> Build(SymbolTable symbols)
        {
            var items = new List<ScriptCompletionItem>();
            foreach (var s in symbols.All.OrderBy(s => s.Name))
            {
                if (s.Category == "Builtin")
                {
                    items.Add(new(s.Name, "variable", "INT (read-only)", "The current FMI cycle number.", s.Name, false));
                    continue;
                }
                var isAspect = s.Category is "Input" or "Output" or "Precondition" or "Resource" or "Control" or "Time";
                var shape = !s.IsArray ? s.DataType : s.Rank == 2 ? $"2-D ARRAY OF {s.DataType}" : $"ARRAY OF {s.DataType}";
                var insert = !s.IsArray ? s.Name : s.Rank == 2 ? $"{s.Name}[${{1:row}}, ${{2:col}}]" : $"{s.Name}[${{1:i}}]";
                var detail = isAspect
                    ? $"{s.Category} aspect · {shape}{(s.IsWritable ? "" : " · read-only")}"
                    : $"{s.Category} {shape}";
                var doc = isAspect
                    ? (s.IsWritable ? "Assign it to send a value downstream." : "Value arriving through this aspect's coupling.")
                    : s.Category switch
                    {
                        "Temp" => "Reset every cycle.",
                        "Static" => "Kept for this function for the whole FMI run.",
                        "Global" => "Shared by all functions for the whole FMI run.",
                        _ => "",
                    };
                items.Add(new(s.Name, isAspect ? "aspect" : "variable", detail, doc, insert, s.IsArray));
            }
            foreach (var (name, insert, doc) in HostFunctions)
            {
                items.Add(new(name, "function", "host function", doc, insert, true));
            }
            foreach (var (name, insert, doc) in MathFunctions)
            {
                items.Add(new(name, "function", "built-in", doc, insert, true));
            }
            foreach (var keyword in Keywords)
            {
                items.Add(new(keyword, "keyword", "keyword", "", keyword, false));
            }
            foreach (var (label, insert, doc) in Snippets)
            {
                items.Add(new(label, "snippet", "snippet", doc, insert, true));
            }
            return items;
        }
    }
}
