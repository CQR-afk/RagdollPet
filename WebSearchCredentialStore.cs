using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace RagdollPet;

internal sealed class WebSearchException(string message) : Exception(message);

internal static class WebSearchCredentialStore
{
    private const int MaxResults = 5;
    private const uint UiForbidden = 0x1;
    private static readonly string KeyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RagdollPet", "ollama-web-search-key.dpapi");
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
    }) { Timeout = TimeSpan.FromSeconds(25) };

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("Kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static bool HasApiKey => !string.IsNullOrWhiteSpace(ReadApiKey());

    public static void SaveApiKey(string key)
    {
        string normalized = key.Trim();
        if (normalized.Length is < 8 or > 2048 || normalized.Any(char.IsWhiteSpace))
            throw new ArgumentException("API key 长度无效，请检查复制的内容。");
        byte[] plain = Encoding.UTF8.GetBytes(normalized);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
            File.WriteAllBytes(KeyPath, TransformWithDpapi(plain, protect: true));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static void ClearSavedApiKey()
    {
        if (File.Exists(KeyPath)) File.Delete(KeyPath);
    }

    public static async Task<string> SearchAsync(string query)
    {
        IReadOnlyList<WebSearchResult> results = await SearchResultsAsync(query, CancellationToken.None);
        return WebSearchManager.FormatContext(results);
    }

    public static async Task<IReadOnlyList<WebSearchResult>> SearchResultsAsync(string query, CancellationToken cancellationToken)
    {
        string cleanQuery = RegexQuery(query);
        if (cleanQuery.Length is < 2 or > 200)
            throw new WebSearchException("网页搜索词为空或过长；请缩短问题后重试。");

        string? apiKey = ReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            string reason = File.Exists(KeyPath)
                ? "已保存的网页搜索密钥无法读取。请点“设置”重新保存 Ollama API key。"
                : "网页搜索尚未配置 Ollama API key。请点“设置”，输入 Ollama 账户创建的 API key 后重试。";
            throw new WebSearchException(reason);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://ollama.com/api/web_search")
        {
            Content = JsonContent.Create(new { query = cleanQuery })
        };
        try
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        catch (FormatException)
        {
            throw new WebSearchException("Ollama API key 格式无效，请点“设置”重新配置。");
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw new WebSearchException("网页搜索连接超时。请检查网络或代理设置后重试。");
        }
        catch (HttpRequestException)
        {
            throw new WebSearchException("无法连接 Ollama 网页搜索服务。请检查网络或代理设置后重试。");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string explanation = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "API key 无效或没有搜索权限；请点“设置”重新配置。",
                    (HttpStatusCode)429 => "网页搜索服务当前请求过多，请稍后重试。",
                    var status when (int)status >= 500 => "网页搜索服务暂时不可用，请稍后重试。",
                    _ => $"网页搜索服务返回 HTTP {(int)response.StatusCode}。"
                };
                throw new WebSearchException(explanation);
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (!document.RootElement.TryGetProperty("results", out var resultArray) ||
                    resultArray.ValueKind != JsonValueKind.Array)
                    throw new WebSearchException("网页搜索服务返回了无法识别的结果格式，请稍后重试。");

                var results = new List<WebSearchResult>();
                foreach (JsonElement item in resultArray.EnumerateArray())
                {
                    if (results.Count >= MaxResults) break;
                    string title = ReadText(item, "title", 160);
                    string content = ReadText(item, "content", 400);
                    string url = ReadText(item, "url", 2048);
                    if (title.Length == 0 || !Uri.TryCreate(url, UriKind.Absolute, out Uri? target) ||
                        (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps) ||
                        target.AbsoluteUri.Length > 700) continue;
                    results.Add(new WebSearchResult(title, target.AbsoluteUri, content, "Ollama Web Search"));
                }

                if (results.Count == 0)
                    throw new WebSearchException("网页搜索服务没有返回可用结果，请换个关键词或稍后重试。");

                return results;
            }
            catch (JsonException)
            {
                throw new WebSearchException("网页搜索服务返回了无法识别的结果格式，请稍后重试。");
            }
            catch (InvalidOperationException)
            {
                throw new WebSearchException("网页搜索服务返回了无法识别的结果格式，请稍后重试。");
            }
            catch (IOException)
            {
                throw new WebSearchException("读取网页搜索结果时连接中断，请检查网络后重试。");
            }
        }
    }

    internal static string? ReadApiKey()
    {
        string? environmentKey = Environment.GetEnvironmentVariable("OLLAMA_API_KEY");
        if (!string.IsNullOrWhiteSpace(environmentKey) && !environmentKey.Any(char.IsWhiteSpace))
            return environmentKey.Trim();
        if (!File.Exists(KeyPath)) return null;

        try
        {
            byte[] protectedBytes = File.ReadAllBytes(KeyPath);
            byte[] plainBytes = TransformWithDpapi(protectedBytes, protect: false);
            try { return Encoding.UTF8.GetString(plainBytes); }
            finally { CryptographicOperations.ZeroMemory(plainBytes); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or
                                   DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static byte[] TransformWithDpapi(byte[] input, bool protect)
    {
        IntPtr inputPointer = Marshal.AllocHGlobal(input.Length);
        DataBlob inputBlob = new() { Size = input.Length, Data = inputPointer };
        DataBlob outputBlob = default;
        try
        {
            Marshal.Copy(input, 0, inputPointer, input.Length);
            bool succeeded = protect
                ? CryptProtectData(ref inputBlob, "RagdollPet Ollama web search key", IntPtr.Zero,
                    IntPtr.Zero, IntPtr.Zero, UiForbidden, out outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, IntPtr.Zero, UiForbidden, out outputBlob);
            if (!succeeded) throw new CryptographicException($"Windows 无法保护或读取 API key（错误 {Marshal.GetLastWin32Error()}）。");

            byte[] output = new byte[outputBlob.Size];
            Marshal.Copy(outputBlob.Data, output, 0, outputBlob.Size);
            return output;
        }
        finally
        {
            if (inputPointer != IntPtr.Zero)
            {
                Marshal.Copy(new byte[input.Length], 0, inputPointer, input.Length);
                Marshal.FreeHGlobal(inputPointer);
            }
            if (outputBlob.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[outputBlob.Size], 0, outputBlob.Data, outputBlob.Size);
                LocalFree(outputBlob.Data);
            }
        }
    }

    private static string ReadText(JsonElement item, string property, int limit)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return "";
        string text = RegexQuery(value.GetString() ?? "");
        return text.Length > limit ? text[..limit] + "…" : text;
    }

    private static string RegexQuery(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");
}

internal sealed class WebSearchSettingsWindow : Window
{
    private sealed record ProviderOption(WebSearchProviderKind Value, string Label);
    private sealed record EngineOption(WebSearchEngine Value, string Label);

    private readonly ComboBox providerSelector = new();
    private readonly ComboBox engineSelector = new();
    private readonly TextBox searxngAddress = new();
    private readonly PasswordBox keyBox = new();
    private readonly TextBlock providerDescription = new();
    private readonly TextBlock message = new();
    private readonly StackPanel searxngPanel = new();
    private readonly StackPanel chromePanel = new();
    private readonly StackPanel ollamaPanel = new();

    private WebSearchSettingsWindow()
    {
        Title = "网页搜索设置";
        Width = 480;
        Height = 620;
        MinWidth = 430;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(255, 250, 246));

        WebSearchSettings settings = WebSearchSettings.Load();
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = "网页搜索设置",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(73, 62, 57)),
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "仅发送本轮搜索词。聊天历史、本地文件和模型内容不会发给搜索服务。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        AddLabel(root, "搜索方式");
        providerSelector.ItemsSource = new[]
        {
            new ProviderOption(WebSearchProviderKind.SearXng, "本地 SearXNG（推荐）"),
            new ProviderOption(WebSearchProviderKind.Chrome, "Chrome 浏览器搜索"),
            new ProviderOption(WebSearchProviderKind.Ollama, "Ollama Web Search API")
        };
        providerSelector.DisplayMemberPath = nameof(ProviderOption.Label);
        providerSelector.SelectedValuePath = nameof(ProviderOption.Value);
        providerSelector.SelectedValue = settings.Provider;
        providerSelector.Padding = new Thickness(7, 5, 7, 5);
        providerSelector.SelectionChanged += (_, _) => RefreshProviderPanel();
        root.Children.Add(providerSelector);
        providerDescription.TextWrapping = TextWrapping.Wrap;
        providerDescription.Margin = new Thickness(0, 7, 0, 10);
        providerDescription.Foreground = new SolidColorBrush(Color.FromRgb(123, 91, 81));
        root.Children.Add(providerDescription);

        AddSection(root, "本地 SearXNG", searxngPanel);
        AddLabel(searxngPanel, "服务地址");
        searxngAddress.Text = settings.SearXngUrl;
        searxngAddress.Padding = new Thickness(8, 6, 8, 6);
        searxngPanel.Children.Add(searxngAddress);
        AddHint(searxngPanel, "默认 http://127.0.0.1:8888。也可填写已运行的远程 SearXNG 地址；该服务将收到当前搜索词。Docker 未安装时不会自动安装。 ");

        AddSection(root, "Chrome 浏览器搜索", chromePanel);
        AddLabel(chromePanel, "搜索引擎");
        engineSelector.ItemsSource = new[]
        {
            new EngineOption(WebSearchEngine.Google, "Google"),
            new EngineOption(WebSearchEngine.Bing, "Bing")
        };
        engineSelector.DisplayMemberPath = nameof(EngineOption.Label);
        engineSelector.SelectedValuePath = nameof(EngineOption.Value);
        engineSelector.SelectedValue = settings.BrowserEngine;
        engineSelector.Padding = new Thickness(7, 5, 7, 5);
        chromePanel.Children.Add(engineSelector);
        AddHint(chromePanel, "Chrome 使用 D 盘发布目录中的团团专用资料夹，不读取日常 Chrome Profile。需要时可在专用窗口手动登录；不自动登录、不处理验证码。 ");
        var browserButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var openBrowser = new Button { Content = "打开团团专用浏览器", Padding = new Thickness(10, 5, 10, 5) };
        openBrowser.Click += async (_, _) =>
        {
            try
            {
                await WebSearchManager.OpenDedicatedBrowserAsync(ReadDraft());
                message.Text = "团团专用浏览器已打开，可在此窗口手动登录。";
            }
            catch (Exception ex) when (ex is WebSearchException or IOException or UnauthorizedAccessException)
            {
                message.Text = ex.Message;
            }
        };
        var closeBrowser = new Button { Content = "关闭专用浏览器", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0) };
        closeBrowser.Click += async (_, _) =>
        {
            await WebSearchManager.CloseDedicatedBrowserAsync();
            message.Text = "团团专用浏览器已关闭。";
        };
        browserButtons.Children.Add(openBrowser);
        browserButtons.Children.Add(closeBrowser);
        chromePanel.Children.Add(browserButtons);

        AddSection(root, "Ollama Web Search", ollamaPanel);
        AddHint(ollamaPanel, "密钥仍使用 Windows DPAPI 保存于本机。手动测试会执行一次 API 搜索，并可能计入用量；仅在本模式被选择或点击测试时调用。 ");
        keyBox.PasswordChar = '●';
        keyBox.Padding = new Thickness(8, 6, 8, 6);
        keyBox.ToolTip = "输入 Ollama 账户 API key；留空会保留现有密钥";
        ollamaPanel.Children.Add(keyBox);
        var ollamaButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var openAccount = new Button { Content = "打开 API key 页面", Padding = new Thickness(10, 5, 10, 5) };
        openAccount.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://ollama.com/settings/keys") { UseShellExecute = true }); }
            catch (Win32Exception) { message.Text = "无法打开浏览器；请手动访问 ollama.com/settings/keys。"; }
        };
        var clearKey = new Button { Content = "清除已保存密钥", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0) };
        clearKey.Click += (_, _) =>
        {
            try
            {
                WebSearchCredentialStore.ClearSavedApiKey();
                keyBox.Clear();
                message.Text = "已清除本机 DPAPI 密钥。";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                message.Text = "无法清除已保存的密钥：" + ex.Message;
            }
        };
        ollamaButtons.Children.Add(openAccount);
        ollamaButtons.Children.Add(clearKey);
        ollamaPanel.Children.Add(ollamaButtons);

        message.TextWrapping = TextWrapping.Wrap;
        message.Foreground = new SolidColorBrush(Color.FromRgb(150, 83, 70));
        message.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(message);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var test = new Button { Content = "测试当前方式", Padding = new Thickness(10, 5, 10, 5) };
        test.Click += async (_, _) => await TestSelectedProviderAsync(test);
        var cancel = new Button { Content = "取消", MinWidth = 70, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        var save = new Button { Content = "保存设置", MinWidth = 82, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        save.Click += (_, _) => SaveSettings();
        buttons.Children.Add(test);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        Content = scroll;
        RefreshProviderPanel();
    }

    public static bool ShowFor(Window owner)
    {
        var dialog = new WebSearchSettingsWindow { Owner = owner };
        return dialog.ShowDialog() == true;
    }

    private async Task TestSelectedProviderAsync(Button testButton)
    {
        try
        {
            testButton.IsEnabled = false;
            WebSearchSettings draft = ReadDraft();
            message.Text = draft.Provider == WebSearchProviderKind.Ollama
                ? "正在执行一次 Ollama 搜索 API 测试（可能计入用量）…"
                : "正在连接所选搜索方式…";
            message.Text = await WebSearchManager.TestProviderAsync(draft);
        }
        catch (Exception ex) when (ex is WebSearchException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            message.Text = ex.Message;
        }
        finally { testButton.IsEnabled = true; }
    }

    private void SaveSettings()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(keyBox.Password))
                WebSearchCredentialStore.SaveApiKey(keyBox.Password);
            WebSearchManager.SaveSettings(ReadDraft());
            DialogResult = true;
        }
        catch (Exception ex) when (ex is WebSearchException or ArgumentException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            message.Text = ex.Message;
        }
    }

    private WebSearchSettings ReadDraft() => new()
    {
        Provider = providerSelector.SelectedValue is WebSearchProviderKind provider ? provider : WebSearchProviderKind.SearXng,
        SearXngUrl = searxngAddress.Text,
        BrowserEngine = engineSelector.SelectedValue is WebSearchEngine engine ? engine : WebSearchEngine.Google
    };

    private void RefreshProviderPanel()
    {
        WebSearchProviderKind provider = ReadDraft().Provider;
        searxngPanel.Visibility = provider == WebSearchProviderKind.SearXng ? Visibility.Visible : Visibility.Collapsed;
        chromePanel.Visibility = provider == WebSearchProviderKind.Chrome ? Visibility.Visible : Visibility.Collapsed;
        ollamaPanel.Visibility = provider == WebSearchProviderKind.Ollama ? Visibility.Visible : Visibility.Collapsed;
        providerDescription.Text = provider switch
        {
            WebSearchProviderKind.Chrome => "使用系统 Chrome 访问 Google 或 Bing。搜索词会直接发送给所选搜索引擎。",
            WebSearchProviderKind.Ollama => "使用 Ollama Web Search API。此服务可能计次；不会作为其他搜索方式失败时的自动备用。",
            _ => "使用本机或自选 SearXNG。默认请求 127.0.0.1，不需要 Ollama API Key。"
        };
    }

    private static void AddSection(Panel parent, string title, StackPanel content)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(240, 223, 214)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 4, 0, 6),
            Child = content
        };
        content.Children.Insert(0, new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(123, 91, 81)),
            Margin = new Thickness(0, 0, 0, 7)
        });
        parent.Children.Add(border);
    }

    private static void AddLabel(Panel parent, string text) => parent.Children.Add(new TextBlock
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(99, 79, 71)),
        Margin = new Thickness(0, 3, 0, 4)
    });

    private static void AddHint(Panel parent, string text) => parent.Children.Add(new TextBlock
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.FromRgb(137, 112, 101)),
        FontSize = 11,
        Margin = new Thickness(0, 6, 0, 0)
    });
}
