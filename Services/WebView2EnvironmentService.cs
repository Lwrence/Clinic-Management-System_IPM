using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CruzNeryClinic.Services
{
    public static class WebView2EnvironmentService
    {
        private static readonly Lazy<Task<CoreWebView2Environment>> EnvironmentTask = new(CreateEnvironmentAsync);

        public static async Task EnsureInitializedAsync(WebView2 webView)
        {
            CoreWebView2Environment environment = await EnvironmentTask.Value;
            await webView.EnsureCoreWebView2Async(environment);
        }

        private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
        {
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CruzNeryClinic",
                "WebView2"
            );

            Directory.CreateDirectory(userDataFolder);
            return await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        }
    }
}
