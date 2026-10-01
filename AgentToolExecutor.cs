using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;

namespace RagdollPet;

internal static class AgentToolExecutor
{
    private const int MaxTextFileBytes = 1024 * 1024;
    private const int MaxCommandSeconds = 60;
    private const int MaxCommandOutputChars = 12000;

    public static readonly object[] ToolDefinitions =
    [
        Function("pet_action", "让团团做一个桌宠动作。仅在用户明确要求或动作非常适合当前互动时调用。", new
        {
            action = new { type = "string", description = "动作名称", @enum = new[] { "paw", "sniff", "sit", "sleep", "wake", "walk", "groom", "stretch", "blink" } }
        }, "action"),
        Function("list_directory", "列出用户指定的单个目录内容。必须使用绝对路径；不得扫描无关目录。", new { path = StringProperty("待列出的目录绝对路径") }, "path"),
        Function("read_file", "读取用户指定的 UTF-8 文本文件（最大 1MB）。", new { path = StringProperty("待读取文件的绝对路径") }, "path"),
        Function("write_file", "按用户明确要求创建或覆盖 UTF-8 文本文件。执行前会显示路径和全部内容并请求确认。", new { path = StringProperty("目标文件绝对路径"), content = StringProperty("写入的完整文本") }, "path", "content"),
        Function("delete_file", "按用户明确要求删除一个文件。执行前会显示路径并请求确认；不能删除目录。", new { path = StringProperty("待删除文件的绝对路径") }, "path"),
        Function("run_powershell", "运行一条用户明确要求的 PowerShell 命令。每次执行前显示完整命令并请求确认；命令以当前用户权限运行，最长 60 秒。", new
        {
            command = StringProperty("完整 PowerShell 命令"),
            timeout_seconds = new { type = "integer", description = "超时秒数，1 到 60，默认 20" }
        }, "command")
    ];

    private static object StringProperty(string description) => new { type = "string", description };

    private static object Function(string name, string description, object properties, params string[] required) => new
    {
        type = "function",
        function = new
        {
            name,
            description,
            parameters = new { type = "object", properties, required, additionalProperties = false }
        }
    };

    public static async Task<string> ExecuteAsync(string name, JsonElement arguments, Window owner, Action<string> petAction)
    {
        try
        {
            return name switch
            {
                "pet_action" => ExecutePetAction(arguments, petAction),
                "list_directory" => ListDirectory(RequiredString(arguments, "path")),
                "read_file" => await ReadFileAsync(RequiredString(arguments, "path")),
                "write_file" => await WriteFileAsync(arguments, owner),
                "delete_file" => DeleteFile(arguments, owner),
                "run_powershell" => await RunPowerShellAsync(arguments, owner),
                _ => $"未执行未知工具：{name}"
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                                   InvalidOperationException or JsonException or Win32Exception)
        {
            return $"操作失败：{ex.Message}";
        }
    }

    private static string ExecutePetAction(JsonElement arguments, Action<string> petAction)
    {
        string requested = RequiredString(arguments, "action");
        if (!TryNormalizePetAction(requested, out string action)) return "动作无效，没有执行。";
        petAction(action);
        return $"已执行桌宠动作：{action}";
    }

    private static bool TryNormalizePetAction(string requested, out string action)
    {
        action = requested.Trim().ToLowerInvariant() switch
        {
            "paw" or "挥爪" or "招手" or "伸爪" => "paw",
            "sniff" or "闻一闻" or "嗅闻" or "闻闻" => "sniff",
            "sit" or "坐下" or "坐着" => "sit",
            "sleep" or "睡觉" or "睡一会儿" => "sleep",
            "wake" or "醒来" or "起床" => "wake",
            "walk" or "散步" or "走动" => "walk",
            "groom" or "洗脸" or "舔爪" or "梳理" => "groom",
            "stretch" or "伸懒腰" or "伸展" => "stretch",
            "blink" or "眨眼" or "眨眨眼" => "blink",
            _ => ""
        };
        return action.Length > 0;
    }

    private static string ListDirectory(string path)
    {
        string fullPath = RequireAbsolutePath(path);
        var directory = new DirectoryInfo(fullPath);
        if (!directory.Exists) return $"目录不存在：{fullPath}";
        var entries = directory.EnumerateFileSystemInfos()
            .OrderBy(item => item is FileInfo ? 1 : 0)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .Select(item => item is DirectoryInfo
                ? $"[目录] {item.Name}"
                : $"[文件] {item.Name} ({FormatBytes(((FileInfo)item).Length)})")
            .ToArray();
        string suffix = directory.EnumerateFileSystemInfos().Skip(200).Any() ? "\n（只显示前 200 项）" : "";
        return entries.Length == 0 ? "目录为空。" : string.Join(Environment.NewLine, entries) + suffix;
    }

    private static async Task<string> ReadFileAsync(string path)
    {
        string fullPath = RequireAbsolutePath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists) return $"文件不存在：{fullPath}";
        if (info.Length > MaxTextFileBytes) return $"文件为 {FormatBytes(info.Length)}，超过文本读取上限 1MB。可以使用 PowerShell 按需读取片段。";
        byte[] bytes = await File.ReadAllBytesAsync(fullPath);
        string text = new UTF8Encoding(false, true).GetString(bytes);
        return Truncate(text, MaxCommandOutputChars);
    }

    private static async Task<string> WriteFileAsync(JsonElement arguments, Window owner)
    {
        string path = RequireAbsolutePath(RequiredString(arguments, "path"));
        string content = RequiredString(arguments, "content");
        string parent = Path.GetDirectoryName(path) ?? throw new ArgumentException("文件路径无效。");
        bool exists = File.Exists(path);
        if (!LocalAgentApprovalWindow.Confirm(owner,
                exists ? "确认覆盖文件" : "确认创建文件",
                $"团团请求{(exists ? "覆盖" : "创建")}此文件。路径：{path}\n内容长度：{content.Length:N0} 个字符。",
                content,
                exists ? "确认覆盖" : "确认创建"))
            return "用户拒绝了文件写入，没有修改文件。";
        Directory.CreateDirectory(parent);
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
        return $"文件已{(exists ? "覆盖" : "创建")}：{path}";
    }

    private static string DeleteFile(JsonElement arguments, Window owner)
    {
        string path = RequireAbsolutePath(RequiredString(arguments, "path"));
        if (!File.Exists(path)) return $"文件不存在：{path}";
        if (!LocalAgentApprovalWindow.Confirm(owner, "确认删除文件",
                $"团团请求删除此单个文件。\n路径：{path}", path, "确认删除"))
            return "用户拒绝了文件删除，文件未更改。";
        File.Delete(path);
        return $"文件已删除：{path}";
    }

    private static async Task<string> RunPowerShellAsync(JsonElement arguments, Window owner)
    {
        string command = RequiredString(arguments, "command");
        int timeoutSeconds = arguments.TryGetProperty("timeout_seconds", out var timeout) && timeout.TryGetInt32(out int n)
            ? Math.Clamp(n, 1, MaxCommandSeconds)
            : 20;
        if (!LocalAgentApprovalWindow.Confirm(owner, "确认运行 PowerShell",
                $"团团请求在当前 Windows 用户权限下运行 PowerShell。最长等待 {timeoutSeconds} 秒。\n工作目录：{Environment.CurrentDirectory}",
                command, "确认运行"))
            return "用户拒绝了 PowerShell 命令，没有运行。";

        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new FileNotFoundException("找不到 Windows PowerShell。", powershell);

        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);

        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("PowerShell 启动失败。");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync();
            string timedOutOutput = await JoinOutputAsync(stdoutTask, stderrTask);
            return $"命令在 {timeoutSeconds} 秒后超时并已终止。\n{Truncate(timedOutOutput, MaxCommandOutputChars)}";
        }

        string output = await JoinOutputAsync(stdoutTask, stderrTask);
        return $"退出代码：{process.ExitCode}\n{(string.IsNullOrWhiteSpace(output) ? "（无输出）" : Truncate(output, MaxCommandOutputChars))}";
    }

    private static async Task<string> JoinOutputAsync(Task<string> stdoutTask, Task<string> stderrTask)
    {
        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        return string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout}\n[stderr]\n{stderr}";
    }

    private static string RequiredString(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"工具参数 {name} 缺失或为空。");
        return value.GetString()!;
    }

    private static string RequireAbsolutePath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("必须提供完整绝对路径。", nameof(path));
        return Path.GetFullPath(path);
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):F1} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):F1} MB",
        >= 1024 => $"{bytes / 1024d:F1} KB",
        _ => $"{bytes} B"
    };

    private static string Truncate(string text, int maxChars) => text.Length <= maxChars
        ? text
        : text[..maxChars] + "\n…（输出已截断）";
}

internal sealed class LocalAgentApprovalWindow : Window
{
    private bool Approved { get; set; }

    private LocalAgentApprovalWindow(string title, string description, string detail, string acceptLabel)
    {
        Title = title;
        Width = 760;
        Height = 600;
        MinWidth = 560;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(255, 250, 246));

        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(73, 62, 57)),
            Margin = new Thickness(0, 0, 0, 10)
        });
        var explanation = new TextBlock
        {
            Text = description + "\n\n请检查下面的完整内容。操作将以当前 Windows 用户权限执行。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(explanation, 1);
        grid.Children.Add(explanation);
        var detailBox = new TextBox
        {
            Text = detail,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(10),
            Background = Brushes.White
        };
        Grid.SetRow(detailBox, 2);
        grid.Children.Add(detailBox);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var reject = new Button { Content = "取消", MinWidth = 90, Padding = new Thickness(12, 7, 12, 7), IsCancel = true };
        var accept = new Button { Content = acceptLabel, MinWidth = 100, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0), IsDefault = false };
        reject.Click += (_, _) => { Approved = false; DialogResult = false; };
        accept.Click += (_, _) => { Approved = true; DialogResult = true; };
        buttons.Children.Add(reject);
        buttons.Children.Add(accept);
        Grid.SetRow(buttons, 3);
        grid.Children.Add(buttons);
        Content = grid;
    }

    public static bool Confirm(Window owner, string title, string description, string detail, string acceptLabel)
    {
        var dialog = new LocalAgentApprovalWindow(title, description, detail, acceptLabel) { Owner = owner };
        return dialog.ShowDialog() == true && dialog.Approved;
    }
}
