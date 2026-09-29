using System.Text.Json;
using Microsoft.AspNetCore.Html;

namespace MyNewsDigest.Web.Services;

/// <summary>
/// Sinh thẻ script/css cho React. Dev: trỏ tới Vite dev server (hot reload).
/// Production: đọc wwwroot/app/manifest.json do "npm run build" tạo ra.
/// </summary>
public class ViteAssets(IWebHostEnvironment env, IConfiguration config)
{
    private const string Entry = "src/main.tsx";
    private IHtmlContent? _cached;

    public IHtmlContent Render()
    {
        var devServer = config["Vite:DevServerUrl"];
        if (env.IsDevelopment() && !string.IsNullOrWhiteSpace(devServer))
        {
            var dev = devServer.TrimEnd('/') + "/app";
            return new HtmlString($$"""
                <script type="module">
                  import RefreshRuntime from '{{dev}}/@react-refresh';
                  RefreshRuntime.injectIntoGlobalHook(window);
                  window.$RefreshReg$ = () => {};
                  window.$RefreshSig$ = () => (type) => type;
                  window.__vite_plugin_react_preamble_installed__ = true;
                </script>
                <script type="module" src="{{dev}}/@vite/client"></script>
                <script type="module" src="{{dev}}/{{Entry}}"></script>
                """);
        }

        if (_cached != null) return _cached;

        var manifestPath = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "app", "manifest.json");
        if (!File.Exists(manifestPath))
            return new HtmlString("<!-- Chưa build ClientApp: chạy 'npm run build' trong thư mục ClientApp -->");

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var entry = doc.RootElement.GetProperty(Entry);
        var tags = new List<string>();
        if (entry.TryGetProperty("css", out var css))
            tags.AddRange(css.EnumerateArray().Select(c => $"<link rel=\"stylesheet\" href=\"/app/{c.GetString()}\" />"));
        tags.Add($"<script type=\"module\" src=\"/app/{entry.GetProperty("file").GetString()}\"></script>");

        var html = new HtmlString(string.Join('\n', tags));
        if (!env.IsDevelopment()) _cached = html;
        return html;
    }
}
