# 团团 2.5D 桌宠

一个以实拍布偶猫“团团”为原型制作的 Windows 透明桌宠。项目采用 WPF 和分层 2.5D 素材，可选连接本机运行的 Ollama 模型。

## 当前功能

- 透明、置顶、可拖动的桌面窗口
- Stand / Sit / Lie / Sleep 基础姿势
- 连续步行动画、脚掌落地锁定与动作过渡
- 眨眼、瞳孔跟随、耳朵抖动与头颈联动
- Stand 五向 DirectionPose：Left / Left3Q / Center / Right3Q / Right
- DirectionPose 中间过渡帧和线性透明度切换
- 托盘菜单与基础互动
- 本地 AI 对话、桌宠动作，以及可单独启停的目录搜索和 PDF 读取技能
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

使用已安装的 Ollama，并可在聊天窗中切换其本地模型。按本机笔记本测试，完整版推荐 `Ornith 1.0 9B Q4_K_M`（约 5.6GB，工具调用测试约 40 token/s）；8GB 显存轻量配置继续默认使用 `qwen3:4b`（约 2.5GB）。`Ornith 1.0 35B Q4_K_M` 可在 32GB RAM + 8GB VRAM 上混合加载，但实测约 3 token/s，仅建议短时实验；选择 35B 时应用会自动将上下文限制为 4K。模型推理只连接 `http://127.0.0.1:11434`，历史对话保存在当前 Windows 用户的 `%LOCALAPPDATA%\RagdollPet\chat-history.json`；为避免上下文过长，每次推理只附加近期完整对话轮次。

Agent 技能栏可分别启停“文件搜索”“PDF 读取与摘要”和“网页搜索”。文件搜索只遍历你指定的目录（最多 8 层、2000 个文件和 5000 个目录项；跳过符号链接），支持按文件名或 UTF-8 文本搜索；读取单个文本文件上限 1MB。PDF 技能在本机提取最多 100 页的可选文本，单次最多 20 页、6000 字符；支持用户指定页码范围。扫描版 PDF 需要 OCR，当前不支持。网页搜索默认关闭；开启后，普通信息问题、解释、比较和推荐会在模型回答前自动搜索，问候语和明确的本地文件操作会跳过。应用只发送当前搜索词（最多 180 字符，并尽量移除邮箱、电话号码和长数字），不发送聊天历史或本地文件。搜索方式保存在 `%LOCALAPPDATA%\RagdollPet\web-search-settings.json`；最多返回 5 条有效结果，聊天中会显示标题、摘要、搜索服务和网址。搜索失败或无结果时，本轮会停止，不调用本地模型生成无来源回答。网页内容按不可信资料处理。创建/覆盖、删除单个文件和运行 PowerShell（最长 60 秒）仍需逐项确认。聊天历史与 PDF 提取内容保存在本机聊天记录中。

### 网页搜索方式

聊天窗的“网页搜索”旁边点“设置”，可选择以下后端。打开网页搜索开关后，应用会先搜索再调用当前本地模型；本地模型不负责决定是否搜索。

- **本地 SearXNG（默认）**：地址默认为 `http://127.0.0.1:8888`，不需要 Ollama API key。轻量包内的 `Tools\start-searxng.ps1` 会启动官方 SearXNG Docker 镜像，启用 JSON API，只绑定本机回环地址；缓存与配置位于 D 盘包目录的 `Tools\searxng`。停止服务运行 `Tools\stop-searxng.ps1`。如果没有 Docker Desktop，团团不会安装它；可以在设置中填入你已有的 SearXNG 地址。
- **Chrome 浏览器搜索**：可选 Google 或 Bing。使用系统已安装的 Chrome 与 Playwright，团团的独立浏览器资料夹位于轻量包的 `WebSearchData\ChromeProfile`（D 盘），不会读取或复制日常 Chrome Profile。普通搜索无需登录；确需登录时只在专用浏览器中手动登录。遇到验证码会停止并提示，不尝试绕过。
- **Ollama Web Search API**：需要 Ollama API key。旧密钥仍以 Windows 当前用户 DPAPI 加密保存在 `%LOCALAPPDATA%\RagdollPet\ollama-web-search-key.dpapi`，密钥格式与读取方式未更改。只有主动选择此模式或点击测试时才会调用该 API；设置会提示测试搜索可能计入用量。其他搜索方式失败时不会自动切换到 Ollama。

选择“测试当前方式”会执行一条短测试搜索，以确认设置有效。对于 Ollama，点击测试可能计入一次搜索用量。SearXNG 和 Chrome 返回的联网结果仍需依赖所选搜索服务和网络可用性。

下一轮模型与技能扩展路线见 [Docs/下一轮迭代路线.md](Docs/下一轮迭代路线.md)。

网页搜索 v2 的当前验证结果与已知问题见 [Docs/网页搜索v2-问题说明.md](Docs/网页搜索v2-问题说明.md)。

首次部署时在 PowerShell 运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\setup-local-agent.ps1
```

脚本使用你已安装的 Ollama，把模型目录设为项目内的 `OllamaModels`（项目位于 D 盘时模型也保存在 D 盘），启动本地服务并下载 Ornith 1.0 9B Q4_K_M。若 Ollama 已在系统托盘运行，请先退出再运行脚本，以确保模型写入项目的 D 盘目录。完成后将鼠标移到猫咪上打开聊天卡片。首次下载约需 5.6GB 网络流量；脚本不会删除已有模型。

### 8GB 显存轻量版

轻量版采用 `qwen3:4b`，模型下载约 2.5GB；它不会删除或覆盖已有模型。模型固定下载到项目目录 `OllamaModels`（本项目位于 `D:\pet` 时即为 `D:\pet\OllamaModels`），不会使用 C 盘的 Ollama 默认模型目录。首次运行脚本前，请从系统托盘退出已运行的 Ollama，再运行 `Tools\setup-lightweight-agent.ps1`；脚本会设置 Ollama 用户级模型目录并启动使用该目录的服务。若要从源码生成独立的 Windows x64 桌宠程序包（需要另装 Ollama）：

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\build-lightweight.ps1
```

程序包输出至 `dist\RagdollPet-Lightweight-8GB`，其中附有模型配置，聊天界面会优先选择 `qwen3:4b`。模型保存在项目目录 `OllamaModels`，不重复塞进桌宠程序包。包内包含 Playwright 的 Windows 驱动和 SearXNG 启停脚本；Chrome 浏览器本身由系统安装，SearXNG 容器则为可选项。

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
