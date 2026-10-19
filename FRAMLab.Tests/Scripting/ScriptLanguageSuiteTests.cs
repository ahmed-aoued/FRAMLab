using System.Xml;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Models;
using FRAMLab.Shared.MainApp.Services.Scripting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FRAMLab.Tests.Scripting;

/// <summary>
/// Port of FRAMLAB/TestFiles/ScriptLanguageTests.cs. The original harness was a
/// standalone top-level program; this version runs the same 50+ assertions inside
/// xUnit and reports all failures at once.
/// </summary>
public class ScriptLanguageSuiteTests
{
    private static string? Var(Function fn, string key)
    {
        var vars = fn.getVariables();
        return vars.TryGetValue(key, out var value) && value.Length > 0 ? value[0] : null;
    }

    [Fact]
    public void LanguageAndAspectSuite()
    {
        var failures = new List<string>();
        void Check(string name, bool cond)
        {
            if (!cond) failures.Add(name);
        }
        void CheckD(string name, bool cond, string detail)
        {
            Check(cond ? name : $"{name} -> {detail}", cond);
        }

        // ---------- 1. Lexer ----------
        var lexer = new STLexer("IF a >= 2 THEN b := 'x'; END_IF;");
        var toks = lexer.Tokenize();
        Check("lexer produces tokens", toks.Count > 5);
        Check("lexer IF keyword", toks[0].Kind == TokenKind.If);
        Check("lexer >= operator", toks.Any(t => t.Kind == TokenKind.Ge));
        Check("lexer := assign", toks.Any(t => t.Kind == TokenKind.Assign));
        Check("lexer string literal", toks.Any(t => t.Kind == TokenKind.StringLiteral && t.Text == "x"));

        var dqLex = new STLexer("s := \"hello\"; GetInput(\"Hazard\");");
        var dqToks = dqLex.Tokenize();
        Check("lexer accepts double-quoted strings", dqLex.Errors.Count == 0
            && dqToks.Any(t => t.Kind == TokenKind.StringLiteral && t.Text == "hello")
            && dqToks.Any(t => t.Kind == TokenKind.StringLiteral && t.Text == "Hazard"));

        // ---------- 2. Parser ----------
        ProgramNode Parse(string src, out List<ScriptDiagnostic> diags)
        {
            var l = new STLexer(src);
            var tk = l.Tokenize();
            var p = new STParser(tk);
            var prog = p.ParseProgram();
            diags = new List<ScriptDiagnostic>();
            diags.AddRange(l.Errors);
            diags.AddRange(p.Diagnostics);
            return prog;
        }

        var prog = Parse("x := 1 + 2 * 3;", out var d1);
        Check("parser no errors", d1.Count == 0);
        Check("parser assignment", prog.Statements[0] is Assignment a && a.Target == "x");
        var bin = (prog.Statements[0] as Assignment)!.Value as BinaryExpr;
        Check("precedence + over *", bin!.Op == "+" && bin.Right is BinaryExpr rb && rb.Op == "*");

        var ifProg = Parse("IF a > 1 THEN b := 1; ELSE b := 2; END_IF;", out var d2);
        Check("IF parse", d2.Count == 0 && ifProg.Statements[0] is IfStatement);

        var forProg = Parse("FOR i := 1 TO 10 BY 2 DO sum := sum + i; END_FOR;", out var d3);
        Check("FOR parse", d3.Count == 0 && forProg.Statements[0] is ForStatement);

        // ---------- 3. Interpreter ----------
        var globals = new Dictionary<string, STValue>();
        var fnXml = "<Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>1</IDNr><FunctionType>0</FunctionType><IDName>f</IDName></Function>";
        var doc = new XmlDocument();
        doc.LoadXml("<FM>" + fnXml + "</FM>");
        var fn = new Function(doc.SelectSingleNode("//Function")!);

        Dictionary<string, STValue> RunProg(string src, List<ScriptVariable> vars)
        {
            var p = Parse(src, out _);
            var model = new ScriptModel();
            model.Variables.AddRange(vars);
            var interp = new STInterpreter(new ScriptExecutionContext(fn, 0, globals), new Dictionary<string, STValue>());
            interp.Run(p, model);
            return new Dictionary<string, STValue>(interp.Outputs);
        }

        var outs1 = RunProg("r := 1 + 2 * 3;", new() { new ScriptVariable { Name = "r", Scope = ScriptScope.Temp, Type = ScriptVarType.REAL, InitialValue = "0", IsOutput = true } });
        Check("interp arithmetic 1+2*3=7", outs1["r"].AsReal() == 7);

        var outs2 = RunProg("r := 7 / 2;", new() { new ScriptVariable { Name = "r", Scope = ScriptScope.Temp, Type = ScriptVarType.REAL, InitialValue = "0", IsOutput = true } });
        Check("interp 7/2=3.5", outs2["r"].AsReal() == 3.5);

        var outs3 = RunProg("IF 5 > 3 THEN r := 1; ELSE r := 2; END_IF;", new() { new ScriptVariable { Name = "r", Scope = ScriptScope.Temp, Type = ScriptVarType.INT, InitialValue = "0", IsOutput = true } });
        Check("interp IF true branch", outs3["r"].AsInt() == 1);

        var outs4 = RunProg("FOR i := 1 TO 10 DO s := s + i; END_FOR;", new() {
            new ScriptVariable { Name = "s", Scope = ScriptScope.Temp, Type = ScriptVarType.INT, InitialValue = "0", IsOutput = true } });
        Check("interp FOR sum 1..10 = 55", outs4["s"].AsInt() == 55);

        var outs5 = RunProg("WHILE n < 5 DO n := n + 1; END_WHILE;", new() {
            new ScriptVariable { Name = "n", Scope = ScriptScope.Temp, Type = ScriptVarType.INT, InitialValue = "0", IsOutput = true } });
        Check("interp WHILE n<5 -> 5", outs5["n"].AsInt() == 5);

        var outs6 = RunProg("s := 'a' + 'b';", new() { new ScriptVariable { Name = "s", Scope = ScriptScope.Temp, Type = ScriptVarType.STRING, InitialValue = "", IsOutput = true } });
        Check("interp string concat", outs6["s"].AsString() == "ab");

        var staticShared = new Dictionary<string, STValue>();
        for (var run = 0; run < 2; run++)
        {
            var p = Parse("c := c + 1;", out _);
            var model = new ScriptModel();
            model.Variables.Add(new ScriptVariable { Name = "c", Scope = ScriptScope.Static, Type = ScriptVarType.INT, InitialValue = "0" });
            var interp = new STInterpreter(new ScriptExecutionContext(fn, run, globals), staticShared);
            if (!staticShared.ContainsKey("c")) staticShared["c"] = STValue.FromInt(0);
            interp.Run(p, model);
        }
        Check("static var persists across cycles (c=2)", staticShared["c"].AsInt() == 2);

        {
            var p = Parse("r := Cycle();", out _);
            var model = new ScriptModel();
            model.Variables.Add(new ScriptVariable { Name = "r", Scope = ScriptScope.Temp, Type = ScriptVarType.INT, InitialValue = "0", IsOutput = true });
            var interp = new STInterpreter(new ScriptExecutionContext(fn, 42, globals), new Dictionary<string, STValue>());
            interp.Run(p, model);
            Check("Cycle() builtin returns 42", interp.Outputs["r"].AsInt() == 42);
        }

        // ---------- 4. Semantic analysis ----------
        var sym = new SymbolTable();
        sym.Add(new Symbol("x", "INT", true, "Temp"));
        sym.Add(new Symbol("name", "STRING", true, "Temp"));
        sym.Add(new Symbol("HazardSig", "REAL", false, "Input"));
        var analyzed = ScriptCompiler.Compile("x := 'oops'; HazardSig := 3; undeclared := 1;", sym);
        Check("semantic: type-mismatch warning", analyzed.Diagnostics.Any(d => d.Severity == "Warning" && d.Message.Contains("assign STRING")));
        Check("semantic: read-only error", analyzed.Diagnostics.Any(d => d.Severity == "Error" && d.Message.Contains("read-only")));
        Check("semantic: undeclared error", analyzed.Diagnostics.Any(d => d.Severity == "Error" && d.Message.Contains("not declared")));

        // ---------- 5. Aspect type consistency ----------
        var f0doc = new XmlDocument();
        f0doc.LoadXml("<FM><Functions><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>0</IDNr><FunctionType>0</FunctionType><IDName>Detect hazard</IDName></Function></Functions></FM>");
        var f1doc = new XmlDocument();
        f1doc.LoadXml("<FM><Functions><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>1</IDNr><FunctionType>0</FunctionType><IDName>Assess severity</IDName></Function></Functions></FM>");
        var f0 = new Function(f0doc.SelectSingleNode("//Function")!);
        var f1 = new Function(f1doc.SelectSingleNode("//Function")!);
        var couplings = new List<Coupling> { new Coupling("0|Hazard signal|1|I", 0, 0, "from", "to", "", "0", "Hazard signal", "1", "I", 0, 0, 0, 0) };
        f0.Script.SyncAspects(couplings, "0");
        f1.Script.SyncAspects(couplings, "1");
        Check("derive: f0 has Output 'Hazard signal'", f0.Script.AspectVariables.Any(a => a.Category == AspectCategory.Output && a.Label == "Hazard signal"));
        Check("derive: f1 has Input 'Hazard signal'", f1.Script.AspectVariables.Any(a => a.Category == AspectCategory.Input && a.Label == "Hazard signal"));
        f0.Script.AspectVariables.First(a => a.Category == AspectCategory.Output).Type = ScriptVarType.REAL;
        f1.Script.AspectVariables.First(a => a.Category == AspectCategory.Input).Type = ScriptVarType.INT;
        var mismatches = AspectVariable.FindTypeMismatches(new List<Function> { f0, f1 }, couplings);
        Check("type-consistency: mismatch detected", mismatches.Count == 1 && mismatches[0].Contains("Hazard signal"));
        f1.Script.AspectVariables.First(a => a.Category == AspectCategory.Input).Type = ScriptVarType.REAL;
        Check("type-consistency: match -> no warning", AspectVariable.FindTypeMismatches(new List<Function> { f0, f1 }, couplings).Count == 0);

        // ---------- 6. XML round-trip of aspect types ----------
        {
            var d = new XmlDocument();
            d.LoadXml("<FM><Functions><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>2</IDNr><FunctionType>0</FunctionType><IDName>g</IDName></Function></Functions></FM>");
            var fnNode = d.SelectSingleNode("//Function")!;
            var f = new Function(fnNode);
            f.Script.Source = "x := 1;";
            f.Script.AspectVariables.Add(new AspectVariable { Category = AspectCategory.Input, Label = "Sig", Type = ScriptVarType.REAL });
            f.Script.WriteXml(d, fnNode);
            Check("xml: aspectTypes written", fnNode.SelectSingleNode("aspectTypes") is not null);
            var f2 = new Function(fnNode);
            Check("xml: aspect type round-trips", f2.Script.AspectVariables.Any(a => a.Label == "Sig" && a.Type == ScriptVarType.REAL));
        }

        // ---------- 7. sanitization + runtime aspect access ----------
        Check("sanitize: spaces -> underscores", AspectVariable.SanitizeIdentifier("Hazard signal") == "Hazard_signal");
        Check("sanitize: hyphens/slashes", AspectVariable.SanitizeIdentifier("Pre-check / level 2") == "Pre_check_level_2");
        Check("sanitize: leading digit", AspectVariable.SanitizeIdentifier("2nd stage") == "_2nd_stage");
        Check("sanitize: empty -> Aspect", AspectVariable.SanitizeIdentifier("   ") == "Aspect");
        Check("sanitize: plain identifier unchanged", AspectVariable.SanitizeIdentifier("Count") == "Count");

        {
            var doc2 = new XmlDocument();
            doc2.LoadXml("<FM><Functions><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>1</IDNr><FunctionType>0</FunctionType><IDName>Assess severity</IDName></Function></Functions></FM>");
            var fnNode = doc2.SelectSingleNode("//Function")!;
            var rfn = new Function(fnNode);
            rfn.Script.AspectVariables.Add(new AspectVariable { Category = AspectCategory.Input, Label = "Hazard signal", Type = ScriptVarType.REAL });
            rfn.Script.AspectVariables.Add(new AspectVariable { Category = AspectCategory.Output, Label = "Assessment result", Type = ScriptVarType.REAL });
            rfn.newVariable("I0_Hazard_signal", new[] { "4.0", "input" });

            var model = new ScriptModel();
            model.Source = "Assessment_result := Hazard_signal * 2.0;";
            var p2 = Parse(model.Source, out var d4);
            Check("aspect script parses clean", d4.Count == 0);

            var syms = new SymbolTable();
            syms.Add(new Symbol("Hazard_signal", "REAL", false, "Input"));
            syms.Add(new Symbol("Assessment_result", "REAL", true, "Output"));
            var an = ScriptCompiler.Compile(model.Source, syms);
            Check("aspect script: no errors", !an.HasErrors);

            var rctx = new ScriptExecutionContext(rfn, 0, new Dictionary<string, STValue>());
            var interp = new STInterpreter(rctx, new Dictionary<string, STValue>());
            interp.Run(p2, model);
            Check("aspect runtime: no runtime issues", rctx.RuntimeIssues.Count == 0);
            Check("aspect runtime: reads Input aspect (4.0*2=8.0)",
                interp.Outputs.ContainsKey("Assessment_result") && interp.Outputs["Assessment_result"].AsReal() == 8.0);

            var badModel = new ScriptModel();
            badModel.Source = "Hazard_signal := 1.0;";
            var bp = Parse(badModel.Source, out _);
            var bctx = new ScriptExecutionContext(rfn, 0, new Dictionary<string, STValue>());
            var binterp = new STInterpreter(bctx, new Dictionary<string, STValue>());
            binterp.Run(bp, badModel);
            Check("aspect runtime: Input write rejected",
                bctx.RuntimeIssues.Any(i => i.Severity == "Error" && i.Message.Contains("read-only")));
        }

        {
            var d = new XmlDocument();
            d.LoadXml("<FM><Functions><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>3</IDNr><FunctionType>0</FunctionType><IDName>h</IDName></Function></Functions></FM>");
            var fnNode = d.SelectSingleNode("//Function")!;
            var f = new Function(fnNode);
            f.Script.AspectVariables.Add(new AspectVariable { Category = AspectCategory.Output, Label = "Result", Type = ScriptVarType.INT });
            f.Script.WriteXml(d, fnNode);
            Check("xml: aspectTypes-only persists", fnNode.SelectSingleNode("aspectTypes") is not null);
            Check("xml: no empty <script> node", fnNode.SelectSingleNode("script") is null);
            Check("xml: no empty <variables> node", fnNode.SelectSingleNode("variables") is null);
            var f3 = new Function(fnNode);
            Check("xml: aspectTypes-only round-trips", f3.Script.AspectVariables.Any(a => a.Label == "Result" && a.Type == ScriptVarType.INT));
        }

        // ---------- 8. aspect variables drive the FMI coupling pipeline ----------
        var scriptService = new ScriptingService(NullLogger<ScriptingService>.Instance);
        Function MakeServiceFn(string id, string name)
        {
            var d = new XmlDocument();
            d.LoadXml($"<FM><Functions><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\">" +
                      $"<IDNr>{id}</IDNr><FunctionType>0</FunctionType><IDName>{name}</IDName></Function></Functions></FM>");
            var sfn = new Function(d.SelectSingleNode("//Function")!);
            sfn.Script.Enabled = true;
            return sfn;
        }

        {
            var sfn = MakeServiceFn("1", "Assess severity");
            sfn.Script.Source = "result := Fire_signal;";
            sfn.Script.AspectVariables.Add(new AspectVariable { Category = AspectCategory.Input, Label = "Fire signal", Type = ScriptVarType.REAL });
            sfn.Script.Variables.Add(new ScriptVariable { Name = "result", Scope = ScriptScope.Temp, Type = ScriptVarType.REAL, InitialValue = "0", IsOutput = true });
            sfn.newVariable("I0_Fire_signal", new[] { "4.0", "propagated" });

            var sctx = scriptService.ExecuteCycle(sfn, 0);
            CheckD("aspect input: sanitized identifier resolves I0_Fire_signal",
                Var(sfn, "result") == "4", $"got '{Var(sfn, "result")}'");
            Check("aspect input: no runtime errors",
                sctx is not null && !sctx.RuntimeIssues.Any(i => i.Severity == "Error"));
        }

        {
            var sfn = MakeServiceFn("0", "Detect fire");
            sfn.Script.Source = "Hazard_signal := 2.5;";
            sfn.Script.AspectVariables.Add(new AspectVariable { Category = AspectCategory.Output, Label = "Hazard signal", Type = ScriptVarType.REAL });
            sfn.metadata.Add(new[] { "Hazard_signal", "0", "", "", "0" });

            scriptService.ExecuteCycle(sfn, 0);
            CheckD("aspect output: writes function variable under the identifier",
                Var(sfn, "Hazard_signal") == "2.5", $"got '{Var(sfn, "Hazard_signal")}'");
            CheckD("aspect output: updates metadata so coupling propagation carries the value",
                sfn.metadata.First(m => m[0] == "Hazard_signal")[1] == "2.5",
                $"got '{sfn.metadata.First(m => m[0] == "Hazard_signal")[1]}'");
        }

        {
            var d = new XmlDocument();
            d.LoadXml("<FM><Functions><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>2</IDNr><FunctionType>0</FunctionType><IDName>Alarm</IDName></Function></Functions>" +
                      "<Inputs><Input orphan=\"true\"><IDNr>0</IDNr><IDName>Manual trigger</IDName><FunctionIDNr>2</FunctionIDNr></Input></Inputs>" +
                      "<Outputs><Output orphan=\"true\"><IDNr>0</IDNr><IDName>Alarm status</IDName><FunctionIDNr>2</FunctionIDNr></Output></Outputs></FM>");
            var sfn = new Function(d.SelectSingleNode("//FM/Functions/Function[IDNr=2]")!);
            sfn.Script.SyncAspects(new List<Coupling>(), "2", AspectVariable.DeclaredFromXml(d, "2"));
            Check("derive: orphan Output aspect available", sfn.Script.AspectVariables.Any(a => a.Category == AspectCategory.Output && a.Label == "Alarm status"));
            Check("derive: orphan Input aspect available", sfn.Script.AspectVariables.Any(a => a.Category == AspectCategory.Input && a.Label == "Manual trigger"));
        }

        {
            var sfn = MakeServiceFn("3", "Dispatch crew");
            sfn.Script.Source = "Total := Total + 1;\nresult := Total;";
            sfn.Script.Variables.Add(new ScriptVariable { Name = "Total", Scope = ScriptScope.Global, Type = ScriptVarType.INT, InitialValue = "0" });
            sfn.Script.Variables.Add(new ScriptVariable { Name = "result", Scope = ScriptScope.Temp, Type = ScriptVarType.INT, InitialValue = "0", IsOutput = true });

            scriptService.Reset();
            var c1 = scriptService.ExecuteCycle(sfn, 0);
            scriptService.ExecuteCycle(sfn, 1);
            CheckD("global var: assignment has no runtime error",
                c1 is not null && !c1.RuntimeIssues.Any(i => i.Severity == "Error"),
                string.Join("|", c1?.RuntimeIssues.Select(i => i.Message) ?? Enumerable.Empty<string>()));
            CheckD("global var: shared value increments across cycles (=2)",
                Var(sfn, "result") == "2", $"got '{Var(sfn, "result")}'");
        }

        Assert.True(failures.Count == 0, $"{failures.Count} failed: {string.Join("; ", failures)}");
    }
}
