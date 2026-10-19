using System.Collections.Generic;
using System.Linq;

namespace FMV_OG.Shared.MainApp.Models
{
    /// <summary>The FRAM aspect category of an aspect variable.</summary>
    public enum AspectCategory
    {
        Input,
        Precondition,
        Resource,
        Control,
        Time,
        Output
    }

    /// <summary>
    /// An aspect variable for a FRAM function, derived from the model's couplings.
    /// E.g. "Assess severity" automatically has "Hazard signal" as an Input and
    /// "Assessment result" as an Output. The user assigns the data type in the editor.
    /// Aspect variables are read-only in the script (Input-ish) or written via the
    /// runtime (Output); their shared identity across functions is the aspect label.
    /// </summary>
    public class AspectVariable
    {
        public AspectCategory Category { get; set; }
        /// <summary>The aspect label, e.g. "Hazard signal". Shared across coupled functions.</summary>
        public string Label { get; set; } = "";
        /// <summary>Data type assigned by the user in the editor. "ANY" = not yet specified.</summary>
        public ScriptVarType? Type { get; set; } = null;

        public string CategoryLetter => Category switch
        {
            AspectCategory.Input => "I",
            AspectCategory.Precondition => "P",
            AspectCategory.Resource => "R",
            AspectCategory.Control => "C",
            AspectCategory.Time => "T",
            AspectCategory.Output => "O",
            _ => "?"
        };

        /// <summary>
        /// The identifier a script uses to reference this aspect. FRAM labels are
        /// human-readable and usually contain spaces ("Hazard signal"), which are not
        /// valid ST identifiers, so we sanitize to e.g. "Hazard_signal".
        /// </summary>
        public string Identifier => SanitizeIdentifier(Label);

        /// <summary>Convert an arbitrary FRAM label into a valid ST identifier.</summary>
        public static string SanitizeIdentifier(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "Aspect";
            var sb = new System.Text.StringBuilder();
            var lastUnderscore = false;
            foreach (var c in label)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                    lastUnderscore = false;
                }
                else if (!lastUnderscore && sb.Length > 0)
                {
                    sb.Append('_');
                    lastUnderscore = true;
                }
            }
            var result = sb.ToString().TrimEnd('_');
            if (result.Length == 0) return "Aspect";
            if (char.IsDigit(result[0])) result = "_" + result;
            return result;
        }

        /// <summary>True when this aspect is an input-ish category (read in the script).</summary>
        public bool IsRead => Category != AspectCategory.Output;

        public AspectVariable Clone() => new AspectVariable { Category = Category, Label = Label, Type = Type };

        /// <summary>
        /// Derive the aspect variables of a function from the model's coupling list.
        /// A coupling Name is "outputFn|label|toFn|toType"; the output function owns the
        /// Output aspect, the target function owns the corresponding I/P/R/C/T aspect.
        /// </summary>
        public static List<AspectVariable> DeriveFor(string functionIdNr, IEnumerable<Coupling> couplings)
            => DeriveFor(functionIdNr, couplings, null);

        /// <summary>
        /// Derive the aspect variables of a function, combining the model's declared
        /// aspects with the coupling list. <paramref name="declared"/> holds the aspects
        /// declared on the function itself (Inputs/Outputs/Preconditions/Resources/
        /// Controls/Times). It is authoritative for the categories and, crucially, keeps
        /// *unconnected* aspects (orphan outputs, inputs not yet coupled) available to the
        /// function's script; the coupling list on its own cannot see those.
        /// </summary>
        public static List<AspectVariable> DeriveFor(
            string functionIdNr,
            IEnumerable<Coupling> couplings,
            IEnumerable<AspectVariable>? declared)
        {
            var result = new List<AspectVariable>();

            if (declared is not null)
            {
                foreach (var d in declared)
                {
                    AddUnique(result, new AspectVariable { Category = d.Category, Label = d.Label });
                }
            }

            foreach (var c in couplings)
            {
                // this function is the OUTPUT side
                if (c.outputFn == functionIdNr)
                {
                    AddUnique(result, new AspectVariable { Category = AspectCategory.Output, Label = c.label });
                }
                // this function is the TARGET (receives an aspect of type toType)
                if (c.toFn == functionIdNr)
                {
                    var cat = c.toType switch
                    {
                        "I" => AspectCategory.Input,
                        "P" => AspectCategory.Precondition,
                        "R" => AspectCategory.Resource,
                        "C" => AspectCategory.Control,
                        "T" => AspectCategory.Time,
                        _ => (AspectCategory?)null,
                    };
                    if (cat is not null)
                    {
                        AddUnique(result, new AspectVariable { Category = cat.Value, Label = c.label });
                    }
                }
            }
            // stable ordering: by category then label
            return result
                .OrderBy(a => a.Category)
                .ThenBy(a => a.Label)
                .ToList();
        }

        /// <summary>
        /// Read the aspects a function declares in the model's aspect lists
        /// (Inputs/Preconditions/Resources/Controls/Times/Outputs). Used by the standalone
        /// Script IDE tab, which has the project XML but not the editor's aspect lists.
        /// </summary>
        public static List<AspectVariable> DeclaredFromXml(System.Xml.XmlDocument projectData, string functionIdNr)
        {
            var result = new List<AspectVariable>();
            void Read(string section, string element, AspectCategory category)
            {
                var nodes = projectData.SelectNodes(
                    $"//FM/{section}/{element}[FunctionIDNr=\"{functionIdNr}\"]/IDName");
                if (nodes is null) return;
                foreach (System.Xml.XmlNode n in nodes)
                {
                    AddUnique(result, new AspectVariable { Category = category, Label = n.InnerText });
                }
            }
            Read("Inputs", "Input", AspectCategory.Input);
            Read("Preconditions", "Precondition", AspectCategory.Precondition);
            Read("Resources", "Resource", AspectCategory.Resource);
            Read("Controls", "Control", AspectCategory.Control);
            Read("Times", "Time", AspectCategory.Time);
            Read("Outputs", "Output", AspectCategory.Output);
            return result;
        }

        private static void AddUnique(List<AspectVariable> list, AspectVariable candidate)
        {
            if (!list.Any(a => a.Category == candidate.Category && a.Label == candidate.Label))
            {
                list.Add(candidate);
            }
        }

        /// <summary>
        /// Reconstruct minimal Coupling objects (identity fields only) from the persisted
        /// Aspects XML. Used by the standalone IDE tab, which loads the model but does not
        /// run the main editor's geometry/coupling pipeline. A coupling Name is
        /// "outputFn|label|toFn|toType" and the Aspect element also carries
        /// outputFn/toFn attributes.
        /// </summary>
        public static List<Coupling> CouplingsFromXml(System.Xml.XmlDocument projectData)
        {
            var couplings = new List<Coupling>();
            var nodes = projectData.SelectNodes("//FM/Aspects/Aspect");
            if (nodes is null) return couplings;
            foreach (System.Xml.XmlNode aspect in nodes)
            {
                var name = aspect.SelectSingleNode("Name")?.InnerText ?? "";
                var parts = name.Split('|');
                var outputFn = parts.Length > 0 ? parts[0] : (aspect.SelectSingleNode("@outputFn")?.Value ?? "");
                var label = parts.Length > 1 ? parts[1] : "";
                var toFn = parts.Length > 2 ? parts[2] : (aspect.SelectSingleNode("@toFn")?.Value ?? "");
                var toType = parts.Length > 3 ? parts[3] : "";
                if (outputFn == "" || toFn == "") continue;
                couplings.Add(new Coupling(
                    name, 0, 0,
                    aspect.SelectSingleNode("@directionX")?.Value ?? "from",
                    aspect.SelectSingleNode("@directionY")?.Value ?? "to",
                    aspect.SelectSingleNode("@notGroup")?.Value ?? "",
                    outputFn, label, toFn, toType, 0, 0, 0, 0));
            }
            return couplings;
        }

        /// <summary>
        /// Type-consistency check across the whole model: for every coupling, the Output
        /// side and the receiving side must agree on the aspect's data type. Returns
        /// human-readable warnings for mismatches (both sides typed but different).
        /// </summary>
        public static List<string> FindTypeMismatches(
            IEnumerable<Function> functions,
            IEnumerable<Coupling> couplings)
        {
            var warnings = new List<string>();
            // map: functionId -> its script aspect types
            var aspectTypes = new Dictionary<string, Dictionary<(AspectCategory, string), ScriptVarType>>();
            foreach (var fn in functions)
            {
                var map = new Dictionary<(AspectCategory, string), ScriptVarType>();
                foreach (var av in fn.Script.AspectVariables)
                {
                    if (av.Type is not null)
                    {
                        map[(av.Category, av.Label)] = av.Type.Value;
                    }
                }
                aspectTypes[fn.IDNr] = map;
            }

            foreach (var c in couplings)
            {
                // the producing function's Output type for this label
                ScriptVarType? outType = null;
                if (aspectTypes.TryGetValue(c.outputFn, out var outMap)
                    && outMap.TryGetValue((AspectCategory.Output, c.label), out var ot))
                {
                    outType = ot;
                }
                // the receiving function's typed aspect
                var cat = c.toType switch
                {
                    "I" => AspectCategory.Input,
                    "P" => AspectCategory.Precondition,
                    "R" => AspectCategory.Resource,
                    "C" => AspectCategory.Control,
                    "T" => AspectCategory.Time,
                    _ => (AspectCategory?)null,
                };
                if (cat is null || outType is null) continue;
                if (aspectTypes.TryGetValue(c.toFn, out var inMap)
                    && inMap.TryGetValue((cat.Value, c.label), out var inType))
                {
                    if (inType != outType)
                    {
                        var outFn = functions.FirstOrDefault(f => f.IDNr == c.outputFn)?.IDName ?? c.outputFn;
                        var inFn = functions.FirstOrDefault(f => f.IDNr == c.toFn)?.IDName ?? c.toFn;
                        warnings.Add(
                            $"Type mismatch on \"{c.label}\": {outFn} outputs {outType} but {inFn} expects {inType} ({cat.Value}).");
                    }
                }
            }
            return warnings;
        }
    }
}
