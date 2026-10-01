param(
    [switch]$IncludeReasoningModel
)

$ErrorActionPreference = 'Stop'

$model = 'qwen3:14b'
$workspaceRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$modelRoot = Join-Path $workspaceRoot 'OllamaModels'
New-Item -ItemType Directory -Path $modelRoot -Force | Out-Null
[Environment]::SetEnvironmentVariable('OLLAMA_MODELS', $modelRoot, 'User')
$env:OLLAMA_MODELS = $modelRoot

$ollama = Get-Command 'ollama' -ErrorAction SilentlyContinue
if (-not $ollama) {
    $candidatePaths = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'),
        (Join-Path $env:ProgramFiles 'Ollama\ollama.exe')
    )
    $ollamaPath = $candidatePaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $ollamaPath) { throw 'Ollama is not installed. Install it from https://ollama.com/download, reopen PowerShell, and rerun this script.' }
} else {
    $ollamaPath = $ollama.Source
}

$api = 'http://127.0.0.1:11434'
try { $null = Invoke-RestMethod -Uri "$api/api/tags" -TimeoutSec 2 }
catch {
    Write-Host 'Starting the local Ollama service...'
    Start-Process -FilePath $ollamaPath -ArgumentList 'serve' -WindowStyle Hidden
    $ready = $false
    for ($i = 0; $i -lt 45; $i++) {
        Start-Sleep -Seconds 1
        try { $null = Invoke-RestMethod -Uri "$api/api/tags" -TimeoutSec 2; $ready = $true; break }
        catch { }
    }
    if (-not $ready) { throw 'Ollama did not start on 127.0.0.1:11434.' }
}

Write-Host "Downloading $model (about 9.3 GB)..."
& $ollamaPath pull $model
if ($LASTEXITCODE -ne 0) { throw "Ollama failed to download $model (exit code $LASTEXITCODE)." }

if ($IncludeReasoningModel) {
    Write-Host 'Downloading optional gpt-oss:20b (about 14 GB). This is a tight fit for a 16 GB GPU; keep context at 8K.'
    & $ollamaPath pull 'gpt-oss:20b'
    if ($LASTEXITCODE -ne 0) { throw "Ollama failed to download gpt-oss:20b (exit code $LASTEXITCODE)." }
}

$installed = Invoke-RestMethod -Uri "$api/api/tags"
if (-not ($installed.models | Where-Object { $_.name -like 'qwen3:14b*' })) {
    throw "The model download finished, but $model is not listed by Ollama."
}

Write-Host ''
Write-Host 'Local agent is ready.' -ForegroundColor Green
Write-Host "Model: $model"
Write-Host "API:   $api (loopback only)"
Write-Host 'Open RagdollPet and right-click the cat to choose the local AI chat.'
if (-not $IncludeReasoningModel) { Write-Host 'Optional: rerun with -IncludeReasoningModel to add gpt-oss:20b.' }
