using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FMV_OG.Shared.MainApp.Services.Logging
{
    /// <summary>
    /// Logging entry point for <c>static</c> members (notably static
    /// <see cref="Microsoft.JSInterop.JSInvokableAttribute"/> callbacks that JS invokes
    /// directly) which can't receive an <see cref="ILogger"/> through DI. The factory is
    /// assigned once in <c>Program.cs</c>; before that it is a no-op.
    /// </summary>
    public static class StaticLog
    {
        public static ILoggerFactory? Factory { get; set; }

        public static ILogger<T> For<T>() =>
            Factory is null ? NullLogger<T>.Instance : Factory.CreateLogger<T>();

        public static ILogger For(string category) =>
            Factory is null ? NullLogger.Instance : Factory.CreateLogger(category);
    }
}
