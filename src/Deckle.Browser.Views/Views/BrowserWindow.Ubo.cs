using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Deckle.Browser.Views;

// ─── Browser window: uBlock Origin's scriptlets, converted by the web engine ──
//
// adblock-rust reads uBlock Origin's scriptlets as resources, and current uBO
// keeps them as ES modules. The conversion Brave does in Node is done here by
// the JavaScript runtime the browser already has: a web view that is never
// shown, on the shared environment, with the folder of the modules mapped to a
// virtual host (Deny: no other origin may read it) and a page of our own in that
// folder. A fixed script (ubo-converter.js) imports scriptlets.js there and
// builds the resources; ExecuteScriptAsync does not await a promise, so the
// script writes its result to a global and this code polls it. Web messages
// stay off, and every request the view makes that is not for the virtual host
// is refused. The view is closed at the end.
public sealed partial class BrowserWindow
{
    /// <summary>The virtual host the modules are served from (the self-check's pages use another).</summary>
    internal const string UboHost = "ubo-resources.test";

    private const string UboPage = "converter.html";
    private static readonly TimeSpan UboTimeout = TimeSpan.FromSeconds(90);

    /// <summary>Converts the modules found under <paramref name="folder"/> (js/resources/scriptlets.js
    /// and what it imports) to the engine's resources, as a JSON array; null when it could not.
    /// Runs on the window's thread, whichever thread calls.</summary>
    public Task<string?> ConvertUboResourcesAsync(string folder)
    {
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = DispatcherQueue.TryEnqueue(async () =>
        {
            try { done.TrySetResult(await ConvertOnUiThreadAsync(folder)); }
            catch (Exception ex)
            {
                DeckleBrowserViewsSource.Log.UboResourcesFailed("convert", ex.GetType().Name);
                done.TrySetResult(null);
            }
        });
        if (!queued) done.TrySetResult(null);
        return done.Task;
    }

    private async Task<string?> ConvertOnUiThreadAsync(string folder)
    {
        string script = ReadConverterScript();
        await File.WriteAllTextAsync(Path.Combine(folder, UboPage),
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>converter</title></head><body></body></html>");

        CoreWebView2Environment environment = await EnvironmentAsync();
        var view = new WebView2 { Visibility = Visibility.Collapsed, Width = 1, Height = 1 };
        PageHost.Children.Add(view);
        try
        {
            await view.EnsureCoreWebView2Async(environment);
            CoreWebView2 core = view.CoreWebView2;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.SetVirtualHostNameToFolderMapping(UboHost, folder, CoreWebView2HostResourceAccessKind.Deny);
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, args) =>
            {
                if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out Uri? uri) || uri.Host != UboHost)
                    args.Response = environment.CreateWebResourceResponse(null, 403, "Refused", "");
            };

            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationCompleted += (_, args) => loaded.TrySetResult(args.IsSuccess);
            core.Navigate($"https://{UboHost}/{UboPage}");
            if (await Task.WhenAny(loaded.Task, Task.Delay(UboTimeout)) != loaded.Task || !loaded.Task.Result)
            {
                DeckleBrowserViewsSource.Log.UboResourcesFailed("page", "not-loaded");
                return null;
            }

            var clock = Stopwatch.StartNew();
            await core.ExecuteScriptAsync(script);
            while (clock.Elapsed < UboTimeout)
            {
                string raw = await core.ExecuteScriptAsync("window.__uboResult");
                if (raw != "null")
                {
                    string result = JsonSerializer.Deserialize<string>(raw) ?? "";
                    if (result.StartsWith("error:", StringComparison.Ordinal))
                    {
                        DeckleBrowserViewsSource.Log.UboResourcesFailed("import", result["error:".Length..]);
                        return null;
                    }
                    int count = JsonDocument.Parse(result).RootElement.GetArrayLength();
                    DeckleBrowserViewsSource.Log.UboResourcesConverted(count, clock.ElapsedMilliseconds);
                    LastUboConversion = (count, clock.ElapsedMilliseconds);
                    return result;
                }
                await Task.Delay(100);
            }
            DeckleBrowserViewsSource.Log.UboResourcesFailed("import", "timeout");
            return null;
        }
        finally
        {
            PageHost.Children.Remove(view);
            view.Close();
        }
    }

    /// <summary>The resource count and the duration in milliseconds of the last conversion this window made.</summary>
    public (int Resources, long Milliseconds) LastUboConversion { get; private set; }

    private static string ReadConverterScript()
    {
        using Stream stream = typeof(BrowserWindow).Assembly.GetManifestResourceStream("Deckle.Browser.Views.ubo-converter.js")
            ?? throw new InvalidOperationException("The converter script is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
