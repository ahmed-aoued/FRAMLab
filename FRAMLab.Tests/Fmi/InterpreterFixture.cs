using System.Xml;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Services;

namespace FRAMLab.Tests.Fmi;

/// <summary>
/// Builds an <see cref="FmiInterpreter"/> wired like StateContainer does at file load:
/// metadata table per function, implicit definitions from legacy metadata merged with
/// the parsed &lt;FMI&gt;&lt;Variables&gt; section, then Initialize.
/// </summary>
internal static class InterpreterFixture
{
    public static (FmiInterpreter Interpreter, MetadataService Metadata, List<Function> Functions) Load(string xfmvPath)
    {
        var doc = new XmlDocument();
        doc.Load(xfmvPath);
        return FromDocument(doc);
    }

    public static (FmiInterpreter Interpreter, MetadataService Metadata, List<Function> Functions) FromXml(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return FromDocument(doc);
    }

    private static (FmiInterpreter, MetadataService, List<Function>) FromDocument(XmlDocument doc)
    {
        var metadata = new MetadataService();
        var functions = new List<Function>();
        foreach (XmlNode fnNode in doc.SelectNodes("//FM/Functions/Function")!)
        {
            if (fnNode.SelectSingleNode("metadata") is not null)
            {
                metadata.CreateMetadataTable(fnNode);
            }
            functions.Add(new Function(fnNode));
        }

        var defService = new FmiDefinitionService();
        var definition = defService.Load(doc);
        //Implicit defs only fill gaps the author did not declare; a declared <Variable>
        //(even a template with function="") always wins over the inferred legacy one.
        var declaredNames = definition.Variables.Select(v => v.Name).ToHashSet();
        foreach (var implicitDef in defService.BuildImplicitDefinitions(functions).Variables)
        {
            if (!declaredNames.Contains(implicitDef.Name))
            {
                definition.Variables.Add(implicitDef);
            }
        }

        var interpreter = new FmiInterpreter(new ExpressionEvaluator(), metadata);
        interpreter.Initialize(definition, functions);
        return (interpreter, metadata, functions);
    }

    public static FmiEvaluationContext Context(List<Function> functions, int cycle)
        => new() { FunctionList = functions, CouplingList = new List<Coupling>(), Cycle = cycle };

    /// <summary>Runs one full cycle: BeginCycle, evaluate every function, EndCycle.</summary>
    public static List<FmiEvaluationResult> RunCycle(FmiInterpreter interpreter, List<Function> functions, int cycle)
    {
        var ctx = Context(functions, cycle);
        interpreter.BeginCycle(ctx);
        var results = functions.Select(fn => interpreter.EvaluateFunction(fn, ctx)).ToList();
        interpreter.EndCycle(ctx);
        return results;
    }
}
