using FRAMLab.Shared;
using Xunit;

namespace FRAMLab.Tests.Fmi;

/// <summary>
/// Golden-file regression: TestFiles/FMV_FeatureTest.xfmv evaluated for 3 cycles must
/// produce byte-identical evaluated keys/values to legacy behavior (no declared variables,
/// no couplings — the strangler path must be a pure superset of the legacy engine).
/// </summary>
public class GoldenFileTests
{
    private static string GoldenFile => Path.Combine(AppContext.BaseDirectory, "TestFiles", "FMV_FeatureTest.xfmv");

    [Fact]
    public void GoldenFile_ThreeCycles_MatchLegacyExpectations()
    {
        var (interpreter, _, functions) = InterpreterFixture.Load(GoldenFile);

        Assert.Equal(5, functions.Count);
        Assert.Empty(interpreter.Definition.LoadDiagnostics);

        //Exact legacy expectations per cycle (fn:key=value), captured from the unmodified engine.
        var expected = new[]
        {
            "0:Probability=0.9", "1:Risk_Score=40", "1:Impact=5", "1:Probability=0.8",
            "2:Decision=0", "3:Effectiveness=NaN", "4:Logged=NaN"
        };

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var results = InterpreterFixture.RunCycle(interpreter, functions, cycle);
            var keys = results.SelectMany(r => r.EvaluatedKeys.Select(kv => $"{r.FunctionId}:{kv.Key}={kv.Value}")).ToList();
            Assert.Equal(expected, keys);
            Assert.All(results, r => Assert.Empty(r.Diagnostics));
        }

        Assert.Equal(3, interpreter.RunState.History.Count);
        for (var i = 0; i < 3; i++)
        {
            var snapshot = interpreter.RunState.History[i];
            Assert.Equal(i, snapshot.Cycle);
            Assert.Equal(7, snapshot.FunctionValues.Values.Sum(d => d.Count));
        }
    }

    [Fact]
    public void GoldenFile_ImplicitDefinitions_SeedInitialValuesOnly()
    {
        var (interpreter, _, functions) = InterpreterFixture.Load(GoldenFile);
        InterpreterFixture.RunCycle(interpreter, functions, 0);

        //FMV_FeatureTest declares nothing in <FMI><Variables>; every definition is implicit
        //(persist=false), so function-scope seeds must equal the legacy initial values and
        //nothing may be promoted to model scope.
        var expected = new Dictionary<string, Dictionary<string, string>>
        {
            ["0"] = new() { ["Probability"] = "0.9" },
            ["1"] = new() { ["Risk_Score"] = "0", ["Impact"] = "5", ["Probability"] = "0.8" },
            ["2"] = new() { ["Decision"] = "0" },
            ["3"] = new() { ["Effectiveness"] = "0.95" },
            ["4"] = new() { ["Logged"] = "1" },
        };
        foreach (var (fnId, keys) in expected)
        {
            var persisted = interpreter.RunState.GetOrCreate(fnId).PersistentVariables;
            foreach (var (key, initial) in keys)
            {
                Assert.Equal(initial, persisted[key].ToLegacyString());
            }
        }
        Assert.Empty(interpreter.RunState.GlobalVariables);
        Assert.All(interpreter.RunState.History, s => Assert.Empty(s.GlobalValues));
    }
}
