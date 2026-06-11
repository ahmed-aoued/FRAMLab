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
builder.Services.AddSingleton<IFmiDefinitionService, FmiDefinitionService>();
builder.Services.AddSingleton<IFmiInterpreter, FmiInterpreter>();
builder.Services.AddSingleton<AgentLevelService>();
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

await builder.Build().RunAsync();
