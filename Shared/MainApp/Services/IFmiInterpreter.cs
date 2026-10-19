namespace FRAMLab.Shared.MainApp.Services
{
    /// <summary>
    /// Per-call dependencies for the interpreter, built by StateContainer.
    /// Passed as a context object (like MetadataService.CalcVariables takes method
    /// arguments) to avoid a circular DI dependency with the singleton StateContainer.
    /// </summary>
    public sealed class FmiEvaluationContext
    {
        public List<Function> FunctionList { get; init; } = new();
        public List<Coupling> CouplingList { get; init; } = new();
        public int Cycle { get; init; }
        public List<string[]> DataRows { get; init; } = new();
        public Dictionary<string, string> DataKeys { get; init; } = new();
    }

    /// <summary>
    /// Wraps FMI metadata evaluation behind a single service. Currently delegates
    /// expression evaluation to IExpressionEvaluator; a future DSL engine replaces the
    /// implementation without changing callers.
    /// </summary>
    public interface IFmiInterpreter
    {
        FmiRunState RunState { get; }
        FmiModelDefinition Definition { get; }

        void Initialize(FmiModelDefinition definition, List<Function> functions);
        void OnFunctionAdded(Function fn);
        void BeginCycle(FmiEvaluationContext ctx);
        FmiEvaluationResult EvaluateFunction(Function function, FmiEvaluationContext ctx);
        void EndCycle(FmiEvaluationContext ctx);
        void Reset(List<Function> functions);
        /// <summary>
        /// Evaluates a single expression. Macro calls resolve against <paramref name="callingFunctionId"/>
        /// (function-scoped macro first, then global); pass "" for a global-only scope.
        /// </summary>
        string EvaluateExpression(string expression, Dictionary<string, string[]> variables, FmiEvaluationContext ctx, string callingFunctionId = "");
    }
}
