using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Services;
using Xunit;

namespace FRAMLab.Tests.Fmi;

/// <summary>
/// Persistent-state semantics: metadata-carried persistence, pure injection, cycle-scope
/// isolation, coercion diagnostics, and reset behavior.
/// </summary>
public class PersistentStateTests
{
    private const string FnXml = "<Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>1</IDNr><FunctionType>0</FunctionType><IDName>F1</IDName>{0}</Function>";

    private static string Model(string fmiVars, string metadataXml)
        => $"<FM><Functions>{string.Format(FnXml, metadataXml)}</Functions><FMI version=\"1\"><Variables>{fmiVars}</Variables></FMI></FM>";

    [Fact]
    public void Persist_MetadataBacked_AccumulatesAcrossCycles()
    {
        //Counter is declared persist=true AND carried by a legacy metadata row (strangler).
        var (interpreter, _, functions) = InterpreterFixture.FromXml(Model(
            "<Variable name=\"Counter\" type=\"number\" scope=\"function\" persist=\"true\" initial=\"5\" />",
            "<metadata><Counter active=\"true\" equation=\"Counter+1\">5</Counter></metadata>"));

        var observed = new List<double>();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            InterpreterFixture.RunCycle(interpreter, functions, cycle);
            observed.Add(interpreter.RunState.GetOrCreate("1").PersistentVariables["Counter"].AsNumber());
        }

        Assert.Equal(new double[] { 6, 7, 8 }, observed.ToArray());
    }

    [Fact]
    public void Persist_PureInjection_SeedsInitialAndInjectsWithoutMetadataRow()
    {
        //Declared persist variable with NO metadata row: value comes only from the definition.
        var (interpreter, _, functions) = InterpreterFixture.FromXml(Model(
            "<Variable name=\"Budget\" type=\"number\" scope=\"function\" persist=\"true\" initial=\"42\" />",
            "<metadata><Other active=\"false\" equation=\"\">0</Other></metadata>"));

        InterpreterFixture.RunCycle(interpreter, functions, 0);

        var state = interpreter.RunState.GetOrCreate("1");
        Assert.Equal(42, state.PersistentVariables["Budget"].AsNumber());
        //Injected into the legacy variable list so equations can read it next cycle.
        Assert.Equal(42.0, functions[0].getVariable("Budget")?.Value);
    }

    [Fact]
    public void CycleScope_IsReseededEveryCycle_NotPersisted()
    {
        var (interpreter, _, functions) = InterpreterFixture.FromXml(Model(
            "<Variable name=\"Tick\" type=\"number\" scope=\"cycle\" initial=\"9\" />",
            "<metadata><Other active=\"false\" equation=\"\">0</Other></metadata>"));

        for (var cycle = 0; cycle < 2; cycle++)
        {
            InterpreterFixture.RunCycle(interpreter, functions, cycle);
            var state = interpreter.RunState.GetOrCreate("1");
            Assert.Equal(9, state.CycleVariables["Tick"].AsNumber());
            Assert.DoesNotContain("Tick", state.PersistentVariables.Keys);
        }
    }

    [Fact]
    public void Coercion_RawStringStored_WarningDiagnosticContainsKeyName()
    {
        //active="false": the eval loop skips the key, so the raw metadata value reaches
        //write-back untouched and fails number coercion.
        var (interpreter, _, functions) = InterpreterFixture.FromXml(Model(
            "<Variable name=\"Score\" type=\"number\" scope=\"function\" persist=\"true\" initial=\"0\" />",
            "<metadata><Score active=\"false\" equation=\"\">abc</Score></metadata>"));

        var results = InterpreterFixture.RunCycle(interpreter, functions, 0);

        var state = interpreter.RunState.GetOrCreate("1");
        var stored = state.PersistentVariables["Score"];
        Assert.Equal(FmiValueType.String, stored.Type);
        Assert.Equal("abc", stored.ToLegacyString());

        var warning = Assert.Single(results.SelectMany(r => r.Diagnostics));
        Assert.Equal(FmiDiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("Score", warning.Source);
    }

    [Fact]
    public void Reset_PersistentReturnsToInitial_GlobalValuesCleared()
    {
        var (interpreter, metadata, functions) = InterpreterFixture.FromXml(Model(
            "<Variable name=\"Counter\" type=\"number\" scope=\"function\" persist=\"true\" initial=\"5\" />" +
            "<Variable name=\"Global_Mode\" type=\"string\" scope=\"model\" persist=\"true\" initial=\"init\" />",
            "<metadata><Counter active=\"true\" equation=\"Counter+1\">5</Counter>" +
            "<Global_Mode active=\"true\" equation=\"Mode\">run</Global_Mode></metadata>"));

        //Seed the legacy Global_ carrier so the reset path has something to clear.
        metadata.MetaDataKeys["Global_Mode"] = "run";
        InterpreterFixture.RunCycle(interpreter, functions, 0);
        Assert.Equal(6, interpreter.RunState.GetOrCreate("1").PersistentVariables["Counter"].AsNumber());

        interpreter.Reset(functions);

        var state = interpreter.RunState.GetOrCreate("1");
        Assert.Equal(5, state.PersistentVariables["Counter"].AsNumber());
        //Global_ *values* are cleared (key registrations stay for the legacy read path).
        Assert.Equal("", metadata.MetaDataKeys["Global_Mode"]);
        //Model-scope variables re-seed to their declared initial value.
        Assert.Equal("init", interpreter.RunState.GlobalVariables["Global_Mode"].ToLegacyString());
        Assert.Empty(interpreter.RunState.History);
    }

    [Fact]
    public void Snapshot_AfterEndCycle_MatchesLiveState()
    {
        var (interpreter, _, functions) = InterpreterFixture.FromXml(Model(
            "<Variable name=\"Counter\" type=\"number\" scope=\"function\" persist=\"true\" initial=\"5\" />",
            "<metadata><Counter active=\"true\" equation=\"Counter+1\">5</Counter></metadata>"));

        InterpreterFixture.RunCycle(interpreter, functions, 0);

        var state = interpreter.RunState.GetOrCreate("1");
        var snapshot = interpreter.RunState.History[^1];
        Assert.Equal(state.PersistentVariables["Counter"].ToLegacyString(),
                     snapshot.PersistentValues["1"]["Counter"].ToLegacyString());
        Assert.Equal(state.OutputValues["Counter"].ToLegacyString(),
                     snapshot.FunctionValues["1"]["Counter"].ToLegacyString());
    }

    [Fact]
    public void PrevCollision_DeclaredVariableName_EmitsInfoDiagnostic()
    {
        var (interpreter, _, functions) = InterpreterFixture.FromXml(Model(
            "<Variable name=\"Prev_Logged\" type=\"string\" scope=\"function\" initial=\"decl\" />",
            "<metadata><Logged active=\"true\" equation=\"\">1</Logged></metadata>"));
        interpreter.EnablePrevVariables = true;

        //Cycle 0 populates PreviousCycleValues; the collision fires at cycle 1's BeginCycle.
        InterpreterFixture.RunCycle(interpreter, functions, 0);
        Assert.Empty(interpreter.PendingDiagnostics);
        InterpreterFixture.RunCycle(interpreter, functions, 1);

        var diagnostic = Assert.Single(interpreter.PendingDiagnostics);
        Assert.Equal(FmiDiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("Prev_Logged", diagnostic.Source);
        Assert.Contains("Fn 1", diagnostic.Source);
    }
}
