namespace FMV_OG.Shared.MainApp.Services
{
    public interface IExpressionEvaluator
    {
        string Eval(string expression, Dictionary<string, string[]> variables, List<string[]> dataRows, Dictionary<string, string> dataKeys);
    }
}
