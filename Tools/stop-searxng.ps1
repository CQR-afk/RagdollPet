$ErrorActionPreference = 'Stop'
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw '未检测到 Docker。SearXNG 没有通过此项目的启动脚本安装。'
}
$searxngPath = Join-Path $PSScriptRoot 'searxng'
$secretPath = Join-Path $searxngPath '.env'
if (-not (Test-Path -LiteralPath $secretPath)) {
    Write-Host 'SearXNG 尚未由本项目的启动脚本配置。'
    exit 0
}
Push-Location $searxngPath
try {
    docker compose --project-name tuantuan-searxng --env-file $secretPath -f (Join-Path $searxngPath 'compose.yaml') down
    if ($LASTEXITCODE -ne 0) { throw '停止 SearXNG 失败；请检查 Docker Desktop 状态。' }
} finally { Pop-Location }
