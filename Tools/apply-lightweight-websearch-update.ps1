$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packageDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'dist\RagdollPet-Lightweight-8GB'))
$stagingDirectory = [System.IO.Path]::GetFullPath((Join-Path $packageDirectory 'Updates'))
$stagedAssembly = Join-Path $stagingDirectory '团团2.5D桌宠.dll'
$targetAssembly = Join-Path $packageDirectory '团团2.5D桌宠.dll'

if (-not $packageDirectory.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '轻量版目录解析到项目文件夹之外，已停止更新。'
}
if (-not (Test-Path -LiteralPath $stagedAssembly -PathType Leaf)) {
    throw "找不到暂存更新：$stagedAssembly"
}
if (-not (Test-Path -LiteralPath $targetAssembly -PathType Leaf)) {
    throw "找不到轻量版程序：$targetAssembly"
}

$running = Get-Process -Name '团团2.5D桌宠' -ErrorAction SilentlyContinue
if ($running) {
    throw '请先正常退出轻量版桌宠，再重新运行此脚本。'
}

Copy-Item -LiteralPath $stagedAssembly -Destination $targetAssembly -Force
Write-Host '网页搜索更新已安装。现在可以从轻量版目录重新启动团团。'
