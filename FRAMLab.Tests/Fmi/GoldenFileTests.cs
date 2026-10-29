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

        //Exact expectations per cycle (fn:key=value). Risk_Score = Probability * Impact = 0.8 * 5 = 4;
        //it was recorded as 40 on a German machine, where "0,8" (culture formatting of a typed number)
        //was misread - variables now always reach equations in invariant culture.
        var expected = new[]
        {
            "0:Probability=0.9", "1:Risk_Score=4", "1:Impact=5", "1:Probability=0.8",
            "2:Decision=0", "3:Effectiveness=NaN", "4:Logged=NaN"
        };

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var results = InterpreterFixture.RunCycle(interpreter, functions, cycle);
            var keys = results.SelectMany(r => r.EvaluatedKeys.Select(kv => $"{r.FunctionId}:{kv.Key}={kv.Value}")).ToList();
            Assert.Equal(expected, keys);
            //Fn 2 ("Decision = Risk_Score>=4?1:0") reads fn 1's key by its bare name, which nothing
            //feeds into fn 2 (a coupling would deliver it as I1_Risk_Score). Legacy silently read 0;
            //it is now reported as an unknown variable and Decision keeps its value (still 0).
            var unknown = Assert.Single(results.SelectMany(r => r.UnknownVariables));
            Assert.Equal(("2", "Decision", "Risk_Score"), (unknown.FunctionId, unknown.Key, unknown.Variable));
            Assert.All(results.Where(r => r.FunctionId != "2"), r => Assert.Empty(r.Diagnostics));
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
