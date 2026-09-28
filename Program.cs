using FMV_OG;
using FMV_OG.Shared;
using FMV_OG.Shared.MainApp.Services;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
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
builder.Services.AddSingleton<AgentLevelService>();
builder.Services.AddSingleton<FMV_OG.Shared.MainApp.Services.Scripting.ScriptingService>();
builder.Services.AddScoped<SvgUtilsInterop>();

builder.Services.AddBlazoredLocalStorage();
builder.Services.AddSingleton<StateContainer>();
builder.Services.AddLocalization(); 

var http = new HttpClient()
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
};
builder.Services.AddScoped(sp => http);
using var response = await http.GetAsync("renderoptions.json");
using var stream = await response.Content.ReadAsStreamAsync();
builder.Configuration.AddJsonStream(stream);

var host = builder.Build();

// link the scripting engine to the central state (avoids a constructor DI cycle)
var stateContainer = host.Services.GetRequiredService<StateContainer>();
stateContainer.Scripting = host.Services.GetRequiredService<FMV_OG.Shared.MainApp.Services.Scripting.ScriptingService>();

await host.RunAsync();
