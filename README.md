# 团团 2.5D 桌宠 v1.15

一个以实拍布偶猫“团团”为原型制作的 Windows 透明桌宠。项目采用 WPF 和分层 2.5D 素材，不依赖 3D 骨骼，也不接入联网大模型。

## 当前功能

- 透明、置顶、可拖动的桌面窗口
- Stand / Sit / Lie / Sleep 基础姿势
- 连续步行动画、脚掌落地锁定与动作过渡
- 眨眼、瞳孔跟随、耳朵抖动与头颈联动
- Stand 五向 DirectionPose：Left / Left3Q / Center / Right3Q / Right
- DirectionPose 中间过渡帧和线性透明度切换
- 托盘菜单与基础互动

## 使用

- 双击 `团团桌宠.exe` 启动。
- 单击猫咪：互动并跳起来。
- 拖动猫咪：移动到屏幕其他位置。
- 右键猫咪：选择摸摸、睡觉、散步或退出。
- 托盘图标双击也可互动；右键可控制状态和退出。

桌宠会在任务栏上方随机散步、停留或睡觉。程序不联网，也不读取个人文件。

## 开发

需要 .NET 8 SDK：

```powershell
dotnet run --project .\RagdollPet.csproj
```

## 发布

生成无需预装 .NET Desktop Runtime 的 Windows x64 版本：

```powershell
dotnet publish .\RagdollPet.csproj -c Release -r win-x64 --self-contained true
```

生成供开发机使用的 framework-dependent 版本：

```powershell
dotnet publish .\RagdollPet.csproj -c Release -r win-x64 --self-contained false
```
