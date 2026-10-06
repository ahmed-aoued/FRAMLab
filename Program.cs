using FMV_OG;
using FMV_OG.Shared;
using FMV_OG.Shared.MainApp.Services;
using FMV_OG.Shared.MainApp.Services.Logging;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using System.Globalization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// --- Logging -----------------------------------------------------------------
// The Blazor WASM host already logs to the browser devtools console. In DEBUG we
// lower the minimum level to Debug and mirror records into an in-memory ring buffer
// (export it from the console with window.FRAMLab.dumpLogs()). Release stays quiet.
#if DEBUG
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddFramLabLogStore();
#else
builder.Logging.SetMinimumLevel(LogLevel.Warning);
#endif

// Framework chatter (routing, localization lookups, render-tree diffs) is very noisy
// at Debug and would drown out application logs and fill the ring buffer. Keep only
// Warning+ from framework categories; FMV_OG.* categories keep the Debug detail.
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Components.RenderTree", LogLevel.None);

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

// renderoptions.json supplies the SVG style keys. Its absence is not fatal, but it
// used to fail silently at startup - capture the error and report it after the host is built.
Exception? renderOptionsError = null;
try
{
    using var response = await http.GetAsync("renderoptions.json");
    response.EnsureSuccessStatusCode();
    using var stream = await response.Content.ReadAsStreamAsync();
    builder.Configuration.AddJsonStream(stream);
}
catch (Exception ex)
{
    renderOptionsError = ex;
    Console.Error.WriteLine($"[FRAMLab] Failed to load renderoptions.json: {ex.Message}");
}

var host = builder.Build();

// Make logging reachable from static members and from the JS export helper.
StaticLog.Factory = host.Services.GetRequiredService<ILoggerFactory>();
#if DEBUG
LogInterop.Store = host.Services.GetRequiredService<LogStore>();
#endif

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FMV_OG.Program");
logger.LogInformation(LogEvents.AppStart,
    "FRAMLab started. Environment={Environment} BaseAddress={BaseAddress}",
    builder.HostEnvironment.Environment, builder.HostEnvironment.BaseAddress);
if (renderOptionsError is not null)
{
    logger.LogError(LogEvents.StartupConfig, renderOptionsError,
        "renderoptions.json could not be loaded; SVG styling falls back to defaults.");
}

// link the scripting engine to the central state (avoids a constructor DI cycle)
var stateContainer = host.Services.GetRequiredService<StateContainer>();
stateContainer.Scripting = host.Services.GetRequiredService<FMV_OG.Shared.MainApp.Services.Scripting.ScriptingService>();

await host.RunAsync();
