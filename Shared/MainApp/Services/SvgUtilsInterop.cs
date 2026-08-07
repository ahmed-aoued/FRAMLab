using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;

namespace FRAMLab.Shared.MainApp.Services
{
    public class SvgUtilsInterop : IAsyncDisposable
    {
        private readonly IJSRuntime _jsRuntime;

        public SvgUtilsInterop(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task<(double width, double height)> GetSvgElementSize(ElementReference element)
        {
            var result = await _jsRuntime.InvokeAsync<BoundingBox?>("SvgUtils.getSvgElementSize", element);
            if (result is null)
            {
                return (0, 0);
            }
            return (result.width, result.height);
        }

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }

        private sealed class BoundingBox
        {
            public double width { get; set; }
            public double height { get; set; }
        }
    }
}
