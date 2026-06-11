namespace FMV_OG.Shared
{
    /// <summary>
    /// Structured runtime state of an FMI run. During the transition the legacy
    /// Function._variables/metadata remain authoritative for what equations see;
    /// this state mirrors them and is the sole authority for typed/persistent variables.
    /// </summary>
    public sealed class FmiRunState
    {
        public int Cycle { get; set; } = -1;
        public Dictionary<string, FmiValue> GlobalVariables { get; } = new();
        public Dictionary<string, FmiFunctionState> FunctionStates { get; } = new(); //key: Function.IDNr
        public List<string> ActiveCouplings { get; } = new();
        public List<FmiCycleSnapshot> History { get; } = new();

        public FmiFunctionState GetOrCreate(string functionId)
        {
            if (!FunctionStates.TryGetValue(functionId, out var state))
            {
                state = new FmiFunctionState { FunctionId = functionId };
                FunctionStates[functionId] = state;
            }
            return state;
        }
    }

    public sealed class FmiFunctionState
    {
        public string FunctionId { get; init; } = "";
        public Dictionary<string, FmiValue> CycleVariables { get; } = new();      //scope=cycle: cleared + reseeded every BeginCycle
        public Dictionary<string, FmiValue> PersistentVariables { get; } = new(); //scope=function persist=true: survive cycles
        public Dictionary<string, FmiValue> IncomingValues { get; } = new();      //mirror of I3_*/In_*/Isum_* aspect variables
        public Dictionary<string, FmiValue> PreviousCycleValues { get; } = new(); //last cycle's OutputValues
        public Dictionary<string, FmiValue> OutputValues { get; } = new();        //current metadata key values (published values)
        public bool WasActivatedThisCycle { get; set; }
        public bool WasEverActive { get; set; }

        public void Clear()
        {
            CycleVariables.Clear();
            PersistentVariables.Clear();
            IncomingValues.Clear();
            PreviousCycleValues.Clear();
            OutputValues.Clear();
            WasActivatedThisCycle = false;
            WasEverActive = false;
        }
    }

    public sealed class FmiCycleSnapshot
    {
        public int Cycle { get; init; }
        public Dictionary<string, Dictionary<string, FmiValue>> FunctionValues { get; init; } = new(); //functionId -> OutputValues copy
        public List<string> ActivatedFunctions { get; init; } = new();
    }

    /// <summary>Result of evaluating one function's metadata for one cycle.</summary>
    public sealed class FmiEvaluationResult
    {
        public string FunctionId { get; init; } = "";
        public List<(string Key, string Value)> EvaluatedKeys { get; } = new(); //every key the loop touched, in loop order
        public List<FmiDiagnostic> Diagnostics { get; } = new();
    }
}
