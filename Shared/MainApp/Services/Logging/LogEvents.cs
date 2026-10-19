using Microsoft.Extensions.Logging;

namespace FRAMLab.Shared.MainApp.Services.Logging
{
    /// <summary>
    /// Central log event ids so related failure sites can be found together when
    /// filtering console output by event id.
    /// </summary>
    public static class LogEvents
    {
        // 1xxx - application lifecycle / startup
        public static readonly EventId AppStart = new(1001, nameof(AppStart));
        public static readonly EventId StartupConfig = new(1002, nameof(StartupConfig));

        // 2xxx - model / file persistence
        public static readonly EventId FileOpen = new(2001, nameof(FileOpen));
        public static readonly EventId FileSave = new(2002, nameof(FileSave));
        public static readonly EventId BackupRestore = new(2003, nameof(BackupRestore));
        public static readonly EventId SelectionImport = new(2004, nameof(SelectionImport));

        // 3xxx - FMI cycle / display
        public static readonly EventId FmiCycle = new(3001, nameof(FmiCycle));
        public static readonly EventId FmiScript = new(3002, nameof(FmiScript));
        public static readonly EventId DisplaySetup = new(3003, nameof(DisplaySetup));

        // 4xxx - scripting / Script IDE
        public static readonly EventId ScriptCompile = new(4001, nameof(ScriptCompile));
        public static readonly EventId ScriptRun = new(4002, nameof(ScriptRun));
        public static readonly EventId ScriptIde = new(4003, nameof(ScriptIde));
        public static readonly EventId ScriptInitialValue = new(4004, nameof(ScriptInitialValue));

        // 5xxx - UI / JS interop
        public static readonly EventId Ui = new(5001, nameof(Ui));
        public static readonly EventId JsInterop = new(5002, nameof(JsInterop));
        public static readonly EventId AgentsLevels = new(5003, nameof(AgentsLevels));
    }
}
