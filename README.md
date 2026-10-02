# 团团 2.5D 桌宠 v1.18

一个以实拍布偶猫“团团”为原型制作的 Windows 透明桌宠。项目采用 WPF 和分层 2.5D 素材，可选连接本机运行的 Ollama 模型。

## 当前功能

- 透明、置顶、可拖动的桌面窗口
- Stand / Sit / Lie / Sleep 基础姿势
- 连续步行动画、脚掌落地锁定与动作过渡
- 眨眼、瞳孔跟随、耳朵抖动与头颈联动
- Stand 五向 DirectionPose：Left / Left3Q / Center / Right3Q / Right
- DirectionPose 中间过渡帧和线性透明度切换
- 托盘菜单与基础互动
- 本地 AI 对话、桌宠动作，以及经确认的本机文件/PowerShell 工具
- 鼠标悬停聊天卡片与多本地模型切换

## 使用

- 双击 `团团桌宠.exe` 启动。
- 单击猫咪：互动并跳起来。
- 拖动猫咪：移动到屏幕其他位置。
- 右键猫咪：选择摸摸、睡觉、散步或退出。
- 鼠标移到猫咪上：显示团团聊天卡片；点击卡片可固定，右上角收起。
- 托盘图标双击也可互动；右键可控制状态和退出。

桌宠会在任务栏上方随机散步、停留或睡觉。鼠标移到团团身上会弹出聊天卡片，移开后短暂延迟收起；点击卡片后会固定显示，右上角可收起。聊天窗口只连接本机 Ollama。经你明确提出并确认后，Agent 也能在当前 Windows 用户权限下读取/写入/删除文件或运行 PowerShell；这些操作会在执行前显示完整目标或命令供你确认。请勿批准不理解的操作。PowerShell 命令可能访问网络或系统资源。

## 本地 AI Agent

使用已安装的 Ollama，并可在聊天窗中切换其本地模型。完整版推荐 `qwen3:14b`（约 9.3GB）；`gpt-oss:20b` 可作为强推理模型（本机实测 8K 上下文下约 12GB 显存）。另提供 8GB 显存轻量配置，默认使用 `qwen3:4b`（Q4_K_M 模型文件约 2.5GB），为显存中的上下文缓存和桌宠渲染留下更多空间。聊天只连接 `http://127.0.0.1:11434`，历史对话保存在当前 Windows 用户的 `%LOCALAPPDATA%\RagdollPet\chat-history.json`。

Agent 工具有：固定桌宠动作；按需列出目录/读取 UTF-8 文本（每个文件最多 1MB）；经逐项确认后创建或覆盖文件、删除单个文件、运行 PowerShell（最长 60 秒）。文件写入/删除与每条命令都会先展示完整目标/内容并要求确认，取消则不执行。Agent 不会自行扫描全盘，也不会自行把文件内容发送到外部服务。

首次部署时在 PowerShell 运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\setup-local-agent.ps1
```

脚本使用你已安装的 Ollama，把模型目录设为 `E:\desktop pets\OllamaModels`，启动本地服务并下载 `qwen3:14b`。完成后将鼠标移到猫咪上打开聊天卡片。首次下载约需 9.3GB 网络流量。若希望额外下载 `gpt-oss:20b`，运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\setup-local-agent.ps1 -IncludeReasoningModel
```

### 8GB 显存轻量版

轻量版采用 `qwen3:4b`，模型下载约 2.5GB；它不会删除或覆盖已有模型。先运行 `Tools\setup-lightweight-agent.ps1`，再使用轻量版程序即可。若要从源码生成独立的 Windows x64 桌宠程序包（需要另装 Ollama）：

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\build-lightweight.ps1
```

程序包输出至 `dist\RagdollPet-Lightweight-8GB`，其中附有模型配置，聊天界面会优先选择 `qwen3:4b`。Ollama 模型单独保存在 Ollama 当前使用的模型目录中，不重复塞进桌宠程序包。

## 开发

需要 .NET 8 SDK：

```powershell
dotnet run --project .\RagdollPet.csproj
```

## 发布

GitHub 的 `v1.18` Release 提供携带 `gpt-oss:20b` 的 Windows x64 便携包。解压后直接双击 `团团2.5D桌宠.exe`；聊天卡片首次打开时会自动启动包内 Ollama 服务，不要求另装 .NET 或 Ollama。模型和推理临时文件都保存在包目录中；建议显卡显存至少 16GB，并预留约 40GB 磁盘空间用于下载、解压和运行。

生成无需预装 .NET Desktop Runtime 的 Windows x64 版本：

```powershell
dotnet publish .\RagdollPet.csproj -c Release -r win-x64 --self-contained true
```

生成供开发机使用的 framework-dependent 版本：

```powershell
dotnet publish .\RagdollPet.csproj -c Release -r win-x64 --self-contained false
```
