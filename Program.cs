using FRAMLab;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Services;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using System.Globalization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<BrowserService>();
builder.Services.AddSingleton<IUndoService, UndoService>();
builder.Services.AddSingleton<IExpressionEvaluator, ExpressionEvaluator>();
builder.Services.AddSingleton<IFMIDisplayConfigService, FMIDisplayConfigService>();
builder.Services.AddSingleton<IMetadataService, MetadataService>();
builder.Services.AddSingleton<IProjectDocumentService, ProjectDocumentService>();
builder.Services.AddSingleton<IFmiDefinitionService, FmiDefinitionService>();
builder.Services.AddSingleton<IMacroRegistry, MacroRegistry>();
builder.Services.AddSingleton<IFmiInterpreter, FmiInterpreter>();
builder.Services.AddSingleton<AgentLevelService>();
builder.Services.AddSingleton<FRAMLab.Shared.MainApp.Services.Scripting.ScriptingService>();
// Live data for api(): its own HttpClient (absolute URLs); API keys come from this browser's localStorage only.
builder.Services.AddSingleton(sp => new FmiDataService(new HttpClient(),
    name => sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>().InvokeAsync<string?>("localStorage.getItem", new object?[] { FmiDataService.SecretStorageKey(name) }).AsTask()));
builder.Services.AddScoped<SvgUtilsInterop>();

builder.Services.AddBlazoredLocalStorage();
builder.Services.AddSingleton<StateContainer>();
builder.Services.AddLocalization();
builder.Services.AddMudServices();

var http = new HttpClient()
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
};
builder.Services.AddScoped(sp => http);
using var response = await http.GetAsync("renderoptions.json");
using var stream = await response.Content.ReadAsStreamAsync();
builder.Configuration.AddJsonStream(stream);

var host = builder.Build();

// Link the scripting engine to the central state after construction (avoids a DI cycle).
var stateContainer = host.Services.GetRequiredService<StateContainer>();
stateContainer.Scripting = host.Services.GetRequiredService<FRAMLab.Shared.MainApp.Services.Scripting.ScriptingService>();
// Connect live data to the state, the equation interpreter and the script engine.
var fmiData = host.Services.GetRequiredService<FmiDataService>();
stateContainer.fmiData = fmiData;
stateContainer.Scripting.Data = fmiData;
if (host.Services.GetRequiredService<IFmiInterpreter>() is FmiInterpreter fmiInterpreter)
{
    fmiInterpreter.Data = fmiData;
}

await host.RunAsync();
