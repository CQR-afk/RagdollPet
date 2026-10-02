using System.Net.Http;
using System.Net.Http.Json;
using System.IO;
using System.Collections.ObjectModel;
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
    private sealed class ChatHistoryStore
    {
        public string? CurrentConversationId { get; set; }
        public List<ChatConversation> Conversations { get; set; } = [];
    }

    private sealed class ChatConversation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "新聊天";
        public string Model { get; set; } = PreferredModel;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public List<JsonElement> Messages { get; set; } = [];
    }

    private sealed class ConversationListItem(string id, string title, string updatedLabel)
    {
        public string Id { get; } = id;
        public string Title { get; } = title;
        public string UpdatedLabel { get; } = updatedLabel;
    }

    private const string DefaultEndpoint = "http://127.0.0.1:11434";
    private const string RecommendedModel = "qwen3:14b";
    private static readonly string PreferredModel = LoadPreferredModel();
    private static readonly bool HasPreferredModelProfile = File.Exists(
        Path.Combine(AppContext.BaseDirectory, "preferred-model.txt"));
    private const int ContextWindow = 8192;
    private const string SystemPrompt = """
        你是 Windows 桌面上的布偶猫团团，和主人用简体中文聊天。语气亲切、自然、简短。
        你能通过工具操控桌宠、列出目录、读取文本文件、创建或覆盖文件、删除单个文件，以及运行 PowerShell。
        只在用户明确要求相应操作时调用文件或 PowerShell 工具；不得因为“帮我看看/处理一下”而推断出写入、删除或运行命令的许可。写入、删除和每一条 PowerShell 命令都会弹出确认框；用户拒绝后不得换一种方式重试。
        读取与列目录也只限用户要求的内容。不要扫描整个电脑、读取凭据/密钥，或将文件内容发送给外部服务。所有本地模型对话只发往本机 Ollama；不得通过命令联网传输数据，除非用户明确要求该联网操作并在确认框中批准完整命令。
        每轮最多请求 4 个工具操作。工具完成后用简短中文总结实际结果，不要声称执行了失败或被拒绝的操作。
        """;
    private const string VoiceSystemPrompt = """
        你是 Windows 桌面上的布偶猫团团。主人刚通过麦克风和你说话，语音已在本机转成文字。
        用简体中文给出温柔、自然、简短的回应，通常一两句话。不要声称听到了文字以外的内容，不要声称执行桌面操作；语音对话不提供文件、命令或其他工具。
        """;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly Action<string> performPetAction;
    private readonly List<object> messages = [];
    private readonly ObservableCollection<ConversationListItem> conversationItems = [];
    private readonly List<ChatConversation> conversations = [];
    private ChatConversation? currentConversation;
    private readonly string historyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RagdollPet", "chat-history.json");
    private bool rebuildingConversationList;
    private bool fullInterface;
    private Window? petOwner;
    private const double CompactWidth = 390;
    private const double CompactHeight = 535;
    private bool busy;
    private bool modelReady;
    private bool refreshingModels;
    private bool pointerOverPet;
    private bool pinnedOpen;
    private string selectedModel = PreferredModel;
    private string endpoint = DefaultEndpoint;
    private readonly DispatcherTimer hideTimer;

    private static string LoadPreferredModel()
    {
        try
        {
            string profilePath = Path.Combine(AppContext.BaseDirectory, "preferred-model.txt");
            string? model = File.Exists(profilePath) ? File.ReadAllText(profilePath).Trim() : null;
            return string.IsNullOrWhiteSpace(model) ? RecommendedModel : model;
        }
        catch
        {
            return RecommendedModel;
        }
    }

    public event Action<string>? VoiceReplyReady;

    public LocalAgentChatWindow(Action<string> performPetAction)
    {
        InitializeComponent();
        this.performPetAction = performPetAction;
        ConversationList.ItemsSource = conversationItems;
        LoadHistory();
        if (currentConversation is null)
        {
            CreateConversation();
            AddBubble("团团", "喵～我准备好陪你聊天啦。", false);
        }
        else
        {
            RestoreCurrentConversation();
        }
        hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            if (!pinnedOpen && !pointerOverPet && !busy && !IsMouseOver) Hide();
        };
        MouseEnter += (_, _) => hideTimer.Stop();
        MouseLeave += (_, _) => ScheduleAutoHide();
        PreviewMouseDown += (_, _) => pinnedOpen = true;
        Loaded += async (_, _) =>
        {
            if (Owner is Window owner)
            {
                petOwner = owner;
                owner.LocationChanged += Owner_LocationChanged;
            }
            PositionBesideOwner();
            await CheckConnectionAsync();
        };
        Closed += (_, _) =>
        {
            hideTimer.Stop();
            if (petOwner is not null) petOwner.LocationChanged -= Owner_LocationChanged;
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
        if (fullInterface)
        {
            CompactToPopup(hideAfter: true);
            return;
        }
        pinnedOpen = false;
        pointerOverPet = false;
        hideTimer.Stop();
        Hide();
    }

    private void FullInterface_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentConversation();
        fullInterface = true;
        pinnedOpen = true;
        hideTimer.Stop();
        if (petOwner is not null) petOwner.LocationChanged -= Owner_LocationChanged;
        Owner = null;
        Topmost = false;
        ShowInTaskbar = true;
        ResizeMode = ResizeMode.CanResize;
        Width = 1220;
        Height = 820;
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, workArea.Width - 32);
        Height = Math.Min(Height, workArea.Height - 32);
        MinWidth = Math.Min(900, Width);
        MinHeight = Math.Min(620, Height);
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
        SidebarColumn.Width = new GridLength(260);
        ConversationSidebar.Visibility = Visibility.Visible;
        FullInterfaceButton.Visibility = Visibility.Collapsed;
        CompactButton.Visibility = Visibility.Visible;
        MinimizeButton.Visibility = Visibility.Visible;
        MaximizeButton.Visibility = Visibility.Visible;
        if (!IsVisible) Show();
        Activate();
    }

    private void Compact_Click(object sender, RoutedEventArgs e) => CompactToPopup(hideAfter: false);

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CompactToPopup(bool hideAfter)
    {
        fullInterface = false;
        SidebarColumn.Width = new GridLength(0);
        ConversationSidebar.Visibility = Visibility.Collapsed;
        FullInterfaceButton.Visibility = Visibility.Visible;
        CompactButton.Visibility = Visibility.Collapsed;
        MinimizeButton.Visibility = Visibility.Collapsed;
        MaximizeButton.Visibility = Visibility.Collapsed;
        Width = CompactWidth;
        Height = CompactHeight;
        MinWidth = 350;
        MinHeight = 460;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        if (petOwner is not null)
        {
            Owner = petOwner;
            petOwner.LocationChanged -= Owner_LocationChanged;
            petOwner.LocationChanged += Owner_LocationChanged;
        }
        Topmost = true;
        if (hideAfter)
        {
            pinnedOpen = false;
            Hide();
        }
        else
        {
            pinnedOpen = true;
            PositionBesideOwner();
            Activate();
        }
    }

    private void Owner_LocationChanged(object? sender, EventArgs e)
    {
        if (IsVisible && !fullInterface) PositionBesideOwner();
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

    private void Header_DragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void LoadHistory()
    {
        try
        {
            if (!File.Exists(historyPath)) return;
            var store = JsonSerializer.Deserialize<ChatHistoryStore>(File.ReadAllText(historyPath));
            if (store is null) return;
            conversations.AddRange(store.Conversations ?? []);
            currentConversation = conversations.FirstOrDefault(item => item.Id == store.CurrentConversationId)
                ?? conversations.OrderByDescending(item => item.UpdatedAt).FirstOrDefault();
            if (currentConversation is not null && !HasPreferredModelProfile)
                selectedModel = currentConversation.Model;
            RefreshConversationList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            StatusText.Text = "本地聊天记录暂时无法读取";
        }
    }

    private void CreateConversation()
    {
        currentConversation = new ChatConversation { Model = selectedModel };
        conversations.Add(currentConversation);
        messages.Clear();
        HistoryPanel.Children.Clear();
        RefreshConversationList();
        SaveCurrentConversation();
    }

    private void RestoreCurrentConversation()
    {
        if (currentConversation is null) return;
        string requestedModel = HasPreferredModelProfile ? PreferredModel : currentConversation.Model;
        selectedModel = ModelSelector.Items.Count == 0 || ModelSelector.Items.Contains(requestedModel)
            ? requestedModel
            : ModelSelector.SelectedItem as string ?? PreferredModel;
        currentConversation.Model = selectedModel;
        messages.Clear();
        messages.AddRange(currentConversation.Messages.Select(item => (object)item.Clone()));
        refreshingModels = true;
        if (ModelSelector.Items.Contains(selectedModel)) ModelSelector.SelectedItem = selectedModel;
        refreshingModels = false;
        RenderTranscript();
        RefreshConversationList();
    }

    private void RenderTranscript()
    {
        HistoryPanel.Children.Clear();
        int visibleMessages = 0;
        foreach (var message in messages)
        {
            JsonElement element;
            try { element = JsonSerializer.SerializeToElement(message); }
            catch (JsonException) { continue; }
            if (!element.TryGetProperty("role", out var roleValue) || roleValue.ValueKind != JsonValueKind.String ||
                !element.TryGetProperty("content", out var contentValue) || contentValue.ValueKind != JsonValueKind.String) continue;
            string role = roleValue.GetString() ?? "";
            string content = contentValue.GetString() ?? "";
            if (role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(content)) continue;
            AddBubble(role == "user" ? "你" : "团团", content, role == "user");
            visibleMessages++;
        }
        if (visibleMessages == 0) AddBubble("团团", "喵～我准备好陪你聊天啦。", false);
        HistoryScroll.ScrollToEnd();
    }

    private void SaveCurrentConversation()
    {
        if (currentConversation is null) return;
        currentConversation.Model = selectedModel;
        currentConversation.Messages = messages.Select(message => JsonSerializer.SerializeToElement(message)).ToList();
        currentConversation.UpdatedAt = DateTime.Now;
        foreach (var message in currentConversation.Messages)
        {
            if (!message.TryGetProperty("role", out var role) || role.GetString() != "user" ||
                !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String) continue;
            string firstUserText = content.GetString()?.Trim() ?? "";
            if (firstUserText.Length > 0)
                currentConversation.Title = firstUserText.Length > 30 ? firstUserText[..30] + "…" : firstUserText;
            break;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
            var store = new ChatHistoryStore
            {
                CurrentConversationId = currentConversation.Id,
                Conversations = conversations.OrderByDescending(item => item.UpdatedAt).ToList()
            };
            string temporaryPath = historyPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, historyPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "无法保存本地聊天记录";
        }
        RefreshConversationList();
    }

    private void RefreshConversationList()
    {
        if (ConversationList is null) return;
        string filter = ConversationSearchBox?.Text.Trim() ?? "";
        rebuildingConversationList = true;
        conversationItems.Clear();
        foreach (var conversation in conversations.OrderByDescending(item => item.UpdatedAt)
                     .Where(item => filter.Length == 0 || item.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            conversationItems.Add(new ConversationListItem(conversation.Id, conversation.Title,
                conversation.UpdatedAt.ToString("MM-dd HH:mm")));
        }
        ConversationList.SelectedItem = conversationItems.FirstOrDefault(item => item.Id == currentConversation?.Id);
        rebuildingConversationList = false;
    }

    private void NewConversation_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        SaveCurrentConversation();
        CreateConversation();
        AddBubble("团团", "新的聊天开始啦，想聊点什么？", false);
    }

    private void ConversationSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshConversationList();

    private void ConversationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (rebuildingConversationList || busy || ConversationList.SelectedItem is not ConversationListItem item ||
            currentConversation?.Id == item.Id) return;
        SaveCurrentConversation();
        currentConversation = conversations.FirstOrDefault(conversation => conversation.Id == item.Id);
        RestoreCurrentConversation();
    }

    private void DeleteConversation_Click(object sender, RoutedEventArgs e)
    {
        if (busy || currentConversation is null) return;
        var answer = System.Windows.MessageBox.Show(this, "删除当前聊天记录？此操作无法撤销。", "团团的小窝",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        conversations.Remove(currentConversation);
        currentConversation = null;
        messages.Clear();
        if (conversations.Count == 0) CreateConversation();
        else
        {
            currentConversation = conversations.OrderByDescending(item => item.UpdatedAt).First();
            RestoreCurrentConversation();
        }
        SaveCurrentConversation();
    }

    private async Task CheckConnectionAsync()
    {
        SetBusy(true, "正在检查本地模型…");
        try
        {
            await EnsureOllamaServiceAsync();
            using var response = await Http.GetAsync($"{endpoint}/api/tags");
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
                ?? models.FirstOrDefault(name => name!.Equals(PreferredModel, StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault(name => name!.Equals("gpt-oss:20b", StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault();
            if (preferred is null)
            {
                modelReady = false;
                ModelSelector.Text = $"尚无模型 · 建议安装 {PreferredModel}";
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

    private async Task EnsureOllamaServiceAsync()
    {
        using var readinessClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        string bundledOllama = Path.Combine(AppContext.BaseDirectory, "Ollama", "ollama.exe");
        string bundledModels = Path.Combine(AppContext.BaseDirectory, "OllamaModels");
        if (!File.Exists(bundledOllama) || !Directory.Exists(bundledModels))
        {
            endpoint = DefaultEndpoint;
            return;
        }

        int firstPort = 11434;
        try
        {
            using var existing = await readinessClient.GetAsync($"{DefaultEndpoint}/api/tags");
            if (existing.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await existing.Content.ReadAsStringAsync());
                bool bundledModelAvailable = document.RootElement.GetProperty("models").EnumerateArray()
                    .Any(model => model.TryGetProperty("name", out var name) &&
                                  name.GetString()?.StartsWith("gpt-oss:20b", StringComparison.OrdinalIgnoreCase) == true);
                if (bundledModelAvailable)
                {
                    endpoint = DefaultEndpoint;
                    return;
                }
                firstPort = 11435;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // No existing local Ollama service is available; start the bundled one below.
        }

        int port = firstPort;
        for (; port <= 11445; port++)
        {
            string candidate = $"http://127.0.0.1:{port}";
            try
            {
                using var occupied = await readinessClient.GetAsync($"{candidate}/api/tags");
                if (!occupied.IsSuccessStatusCode) continue;
                using var document = JsonDocument.Parse(await occupied.Content.ReadAsStringAsync());
                bool hasModel = document.RootElement.GetProperty("models").EnumerateArray()
                    .Any(model => model.TryGetProperty("name", out var name) &&
                                  name.GetString()?.StartsWith("gpt-oss:20b", StringComparison.OrdinalIgnoreCase) == true);
                if (hasModel)
                {
                    endpoint = candidate;
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                endpoint = candidate;
                break;
            }
        }
        if (port > 11445) return;

        string ollamaHome = Path.GetDirectoryName(bundledOllama)!;
        string temporaryDirectory = Path.Combine(AppContext.BaseDirectory, "OllamaTemp");
        Directory.CreateDirectory(temporaryDirectory);

        var start = new System.Diagnostics.ProcessStartInfo(bundledOllama, "serve")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            WorkingDirectory = ollamaHome
        };
        start.Environment["OLLAMA_MODELS"] = bundledModels;
        start.Environment["OLLAMA_HOST"] = $"127.0.0.1:{port}";
        start.Environment["OLLAMA_NO_CLOUD"] = "1";
        start.Environment["OLLAMA_TMPDIR"] = temporaryDirectory;
        start.Environment["TEMP"] = temporaryDirectory;
        start.Environment["TMP"] = temporaryDirectory;
        System.Diagnostics.Process.Start(start)?.Dispose();

        for (int attempt = 0; attempt < 45; attempt++)
        {
            await Task.Delay(1000);
            try
            {
                using var response = await readinessClient.GetAsync($"{endpoint}/api/tags");
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Keep waiting while the bundled local service initializes.
            }
        }
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e) => await CheckConnectionAsync();

    private async void ModelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshingModels || ModelSelector.SelectedItem is not string model || model == selectedModel) return;
        selectedModel = model;
        if (currentConversation is not null) SaveCurrentConversation();
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
        SaveCurrentConversation();
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
                    SaveCurrentConversation();
                    needsFinalAnswer = false;
                    break;
                }

                messages.Add(new { role = "assistant", content = reply, tool_calls = calls.Clone() });
                SaveCurrentConversation();
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
                    SaveCurrentConversation();
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
                SaveCurrentConversation();
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

    public async Task<string?> GenerateVoiceReplyAsync(string spokenText)
    {
        if (busy) return null;
        string text = spokenText.Trim();
        if (text.Length == 0) return null;
        object? voiceMessage = null;
        try
        {
            if (!modelReady) await CheckConnectionAsync();
            if (!modelReady) return null;

            voiceMessage = new { role = "user", content = text };
            messages.Add(voiceMessage);
            SaveCurrentConversation();
            AddBubble("你", text, true);
            SetBusy(true, $"团团正在用本地模型 {selectedModel} 回应语音…");
            bool disableThinking = selectedModel.StartsWith("qwen3", StringComparison.OrdinalIgnoreCase);
            using var response = await PostChatAsync(includeTools: false, systemPrompt: VoiceSystemPrompt,
                think: disableThinking ? false : null);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var assistant = document.RootElement.GetProperty("message");
            string reply = assistant.TryGetProperty("content", out var content) ? content.GetString() ?? "" : "";
            reply = reply.Trim();
            if (reply.Length == 0) reply = "喵～我听到啦。";
            messages.Add(new { role = "assistant", content = reply });
            SaveCurrentConversation();
            AddBubble("团团", reply, false);
            VoiceReplyReady?.Invoke(reply);
            SetBusy(false, $"本地模型：{selectedModel}");
            return reply;
        }
        catch (Exception)
        {
            if (voiceMessage is not null)
            {
                int failedMessageIndex = messages.LastIndexOf(voiceMessage);
                if (failedMessageIndex >= 0) messages.RemoveAt(failedMessageIndex);
            }
            SetBusy(false, "本地模型暂时没有回应语音。");
            return null;
        }
    }

    private Task<HttpResponseMessage> PostChatAsync(bool includeTools, string? systemPrompt = null, bool? think = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = selectedModel,
            ["stream"] = false,
            ["messages"] = new object[] { new { role = "system", content = systemPrompt ?? SystemPrompt } }.Concat(messages).ToArray(),
            ["options"] = new { num_ctx = ContextWindow, temperature = 0.65 }
        };
        if (includeTools)
        {
            payload["tools"] = AgentToolExecutor.ToolDefinitions;
        }
        if (think.HasValue) payload["think"] = think.Value;
        return Http.PostAsJsonAsync($"{endpoint}/api/chat", payload);
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
