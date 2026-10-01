using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;

namespace RagdollPet;

public partial class LocalAgentChatWindow : Window
{
    private const string Endpoint = "http://127.0.0.1:11434";
    private const string RecommendedModel = "qwen3:14b";
    private const int ContextWindow = 8192;
    private const string SystemPrompt = """
        你是 Windows 桌面上的布偶猫团团，和主人用简体中文聊天。语气亲切、自然、简短。
        你能通过工具操控桌宠、列出目录、读取文本文件、创建或覆盖文件、删除单个文件，以及运行 PowerShell。
        只在用户明确要求相应操作时调用文件或 PowerShell 工具；不得因为“帮我看看/处理一下”而推断出写入、删除或运行命令的许可。写入、删除和每一条 PowerShell 命令都会弹出确认框；用户拒绝后不得换一种方式重试。
        读取与列目录也只限用户要求的内容。不要扫描整个电脑、读取凭据/密钥，或将文件内容发送给外部服务。所有本地模型对话只发往本机 Ollama；不得通过命令联网传输数据，除非用户明确要求该联网操作并在确认框中批准完整命令。
        每轮最多请求 4 个工具操作。工具完成后用简短中文总结实际结果，不要声称执行了失败或被拒绝的操作。
        """;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly Action<string> performPetAction;
    private readonly List<object> messages = [];
    private bool busy;
    private bool modelReady;
    private bool refreshingModels;
    private bool pointerOverPet;
    private bool pinnedOpen;
    private string selectedModel = RecommendedModel;
    private readonly DispatcherTimer hideTimer;

    public LocalAgentChatWindow(Action<string> performPetAction)
    {
        InitializeComponent();
        this.performPetAction = performPetAction;
        hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            if (!pinnedOpen && !pointerOverPet && !busy && !IsMouseOver) Hide();
        };
        MouseEnter += (_, _) => hideTimer.Stop();
        MouseLeave += (_, _) => ScheduleAutoHide();
        PreviewMouseDown += (_, _) => pinnedOpen = true;
        AddBubble("团团", "喵～我准备好陪你聊天啦。", false);
        Loaded += async (_, _) =>
        {
            if (Owner is Window owner) owner.LocationChanged += Owner_LocationChanged;
            PositionBesideOwner();
            await CheckConnectionAsync();
        };
        Closed += (_, _) =>
        {
            hideTimer.Stop();
            if (Owner is Window owner) owner.LocationChanged -= Owner_LocationChanged;
        };
    }

    public void SetPetHover(bool isOver)
    {
        pointerOverPet = isOver;
        if (isOver) hideTimer.Stop();
        else ScheduleAutoHide();
    }

    public void ShowForPetHover()
    {
        if (!IsVisible) Show();
        PositionBesideOwner();
        if (!pinnedOpen) hideTimer.Stop();
    }

    public void OpenPinned()
    {
        pinnedOpen = true;
        pointerOverPet = false;
        hideTimer.Stop();
        if (!IsVisible) Show();
        PositionBesideOwner();
        Activate();
        InputBox.Focus();
    }

    private void ClosePopup_Click(object sender, RoutedEventArgs e)
    {
        pinnedOpen = false;
        pointerOverPet = false;
        hideTimer.Stop();
        Hide();
    }

    private void Owner_LocationChanged(object? sender, EventArgs e)
    {
        if (IsVisible) PositionBesideOwner();
    }

    private void PositionBesideOwner()
    {
        if (Owner is not Window owner) return;
        var work = SystemParameters.WorkArea;
        double rightSide = owner.Left + owner.Width + 10;
        Left = rightSide + Width <= work.Right - 8 ? rightSide : owner.Left - Width - 10;
        Left = Math.Clamp(Left, work.Left + 8, work.Right - Width - 8);
        Top = Math.Clamp(owner.Top + 12, work.Top + 8, work.Bottom - Height - 8);
    }

    private void ScheduleAutoHide()
    {
        if (!pinnedOpen && !pointerOverPet && !busy && IsVisible) hideTimer.Start();
    }

    private async Task CheckConnectionAsync()
    {
        SetBusy(true, "正在检查本地模型…");
        try
        {
            using var response = await Http.GetAsync($"{Endpoint}/api/tags");
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var models = document.RootElement.GetProperty("models").EnumerateArray()
                .Select(item => item.TryGetProperty("name", out var name) ? name.GetString() : null)
                .Where(name => name is not null)
                .ToArray();
            refreshingModels = true;
            ModelSelector.ItemsSource = models.Cast<object>().ToArray();
            string? preferred = models.FirstOrDefault(name => name!.Equals(selectedModel, StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault(name => name!.Equals(RecommendedModel, StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault(name => name!.Equals("gpt-oss:20b", StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault();
            if (preferred is null)
            {
                modelReady = false;
                ModelSelector.Text = "尚无模型 · 建议安装 qwen3:14b";
                SetBusy(false, "Ollama 服务正常，但尚未下载模型。安装后点“重试连接”。");
                refreshingModels = false;
                return;
            }
            ModelSelector.SelectedItem = preferred;
            ModelSelector.Text = preferred;
            selectedModel = preferred!;
            modelReady = true;
            refreshingModels = false;
            SetBusy(false, $"本地模型已就绪：{selectedModel} · 仅连接 127.0.0.1");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            modelReady = false;
            SetBusy(false, "本地模型未连接。请安装/启动 Ollama，然后点“重试连接”。");
        }
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e) => await CheckConnectionAsync();

    private async void ModelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshingModels || ModelSelector.SelectedItem is not string model || model == selectedModel) return;
        selectedModel = model;
        messages.Clear();
        HistoryPanel.Children.Clear();
        AddBubble("团团", $"切换到本地模型 {selectedModel}，我们重新开始这一段聊天吧。", false);
        await CheckConnectionAsync();
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendMessageAsync();

    private async void InputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }

    private async Task SendMessageAsync()
    {
        string text = InputBox.Text.Trim();
        if (text.Length == 0 || busy) return;

        InputBox.Clear();
        AddBubble("你", text, true);
        messages.Add(new { role = "user", content = text });
        SetBusy(true, "团团正在想…");
        try
        {
            string reply = "";
            int operationsRun = 0;
            bool needsFinalAnswer = false;
            for (int round = 0; round < 4; round++)
            {
                using var response = await PostChatAsync(includeTools: true);
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var assistant = document.RootElement.GetProperty("message");
                reply = assistant.TryGetProperty("content", out var content) ? content.GetString() ?? "" : "";
                if (!assistant.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array || calls.GetArrayLength() == 0)
                {
                    messages.Add(new { role = "assistant", content = reply });
                    needsFinalAnswer = false;
                    break;
                }

                messages.Add(new { role = "assistant", content = reply, tool_calls = calls.Clone() });
                foreach (var call in calls.EnumerateArray().Take(4))
                {
                    var function = call.GetProperty("function");
                    string name = function.GetProperty("name").GetString() ?? "";
                    JsonElement arguments = function.TryGetProperty("arguments", out var args) ? args : default;
                    string result;
                    if (operationsRun >= 4)
                    {
                        result = "本轮工具操作达到 4 次上限，此请求没有执行。";
                    }
                    else
                    {
                        result = await AgentToolExecutor.ExecuteAsync(name, arguments, this, performPetAction);
                        operationsRun++;
                    }
                    messages.Add(new { role = "tool", tool_name = name, content = result });
                }
                needsFinalAnswer = true;
                if (operationsRun >= 4) break;
            }

            if (needsFinalAnswer)
            {
                using var final = await PostChatAsync(includeTools: false);
                final.EnsureSuccessStatusCode();
                using var finalDoc = JsonDocument.Parse(await final.Content.ReadAsStringAsync());
                var finalMessage = finalDoc.RootElement.GetProperty("message");
                reply = finalMessage.TryGetProperty("content", out var finalContent) ? finalContent.GetString() ?? "" : "";
                messages.Add(new { role = "assistant", content = reply });
            }

            AddBubble("团团", string.IsNullOrWhiteSpace(reply) ? "喵？我刚刚走神了一下。" : reply.Trim(), false);
            SetBusy(false, $"本地模型：{selectedModel}");
        }
        catch (TaskCanceledException)
        {
            AddBubble("团团", "这次思考花的时间有点久，稍后再试试吧。", false);
            SetBusy(false, "本地模型响应超时；可以重试或缩短问题。");
        }
        catch (HttpRequestException ex)
        {
            AddBubble("团团", "我暂时连不上本地模型。", false);
            modelReady = false;
            SetBusy(false, $"连接本机 Ollama 失败：{ex.Message}");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            AddBubble("团团", "我刚才没组织好语言，咱们再试一次？", false);
            SetBusy(false, "模型返回格式无法识别，请确认 Ollama 和模型版本正常。");
        }
    }

    private Task<HttpResponseMessage> PostChatAsync(bool includeTools)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = selectedModel,
            ["stream"] = false,
            ["messages"] = new object[] { new { role = "system", content = SystemPrompt } }.Concat(messages).ToArray(),
            ["options"] = new { num_ctx = ContextWindow, temperature = 0.65 }
        };
        if (includeTools)
        {
            payload["tools"] = AgentToolExecutor.ToolDefinitions;
        }
        return Http.PostAsJsonAsync($"{Endpoint}/api/chat", payload);
    }

    private void SetBusy(bool value, string status)
    {
        busy = value;
        StatusText.Text = status;
        InputBox.IsEnabled = !value && modelReady;
        SendButton.IsEnabled = !value && modelReady;
        ReconnectButton.IsEnabled = !value;
        if (!value) ScheduleAutoHide();
    }

    private void AddBubble(string sender, string text, bool isUser)
    {
        var bubble = new Border
        {
            Background = isUser ? new SolidColorBrush(Color.FromRgb(249, 225, 215)) : new SolidColorBrush(Color.FromRgb(255, 243, 230)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(244, 226, 215)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(isUser ? 35 : 0, 5, isUser ? 0 : 35, 7),
            HorizontalAlignment = isUser ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left,
            MaxWidth = 330
        };
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = sender == "团团" ? "🐾 团团" : "你", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(144, 108, 99)) });
        content.Children.Add(new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(81, 65, 63)) });
        bubble.Child = content;
        HistoryPanel.Children.Add(bubble);
        HistoryScroll.ScrollToEnd();
    }
}
