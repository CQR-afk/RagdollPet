$ErrorActionPreference = 'Stop'
$searxngPath = Join-Path $PSScriptRoot 'searxng'
$composePath = Join-Path $searxngPath 'compose.yaml'
$settingsPath = Join-Path $searxngPath 'settings.yml'
$secretPath = Join-Path $searxngPath '.env'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw '未检测到 Docker。此脚本不会自动安装 Docker；可以先使用 Chrome 搜索，或在网页搜索设置中填写可用的 SearXNG 地址。'
}
if (-not (Test-Path -LiteralPath $composePath) -or -not (Test-Path -LiteralPath $settingsPath)) {
    throw 'SearXNG 配置文件不完整。请确认 Tools\searxng 文件夹与此脚本位于一起。'
}
docker compose version *> $null
if ($LASTEXITCODE -ne 0) { throw '当前 Docker 未提供 Compose 插件，请安装/更新 Docker Desktop 后再试。' }

if (-not (Test-Path -LiteralPath $secretPath)) {
    $randomBytes = New-Object byte[] 32
    $randomGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $randomGenerator.GetBytes($randomBytes) }
    finally { $randomGenerator.Dispose() }
    $secret = [Convert]::ToBase64String($randomBytes)
    [IO.File]::WriteAllText($secretPath, "SEARXNG_SECRET=$secret`n", [Text.UTF8Encoding]::new($false))
}
New-Item -ItemType Directory -Path (Join-Path $searxngPath 'data') -Force | Out-Null
Push-Location $searxngPath
try {
    docker compose --project-name tuantuan-searxng --env-file $secretPath -f $composePath up -d
    if ($LASTEXITCODE -ne 0) { throw 'SearXNG 容器启动失败。可运行 docker compose logs searxng 查看原因。' }
} finally { Pop-Location }
Write-Host 'SearXNG 已启动，仅监听本机 127.0.0.1:8888。' -ForegroundColor Green
Write-Host '先等待容器就绪，再在团团的“网页搜索设置”中点击“测试当前方式”。'
