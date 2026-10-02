$ErrorActionPreference = 'Stop'

$model = 'hf.co/ornith-ai/Ornith-1.0-9B-GGUF:Q4_K_M'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
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
try {
    $null = Invoke-RestMethod -Uri "$api/api/tags" -TimeoutSec 2
    throw "Ollama is already running. Quit it from the system tray, then rerun this script so the model is saved under $modelRoot. No model was downloaded."
}
catch {
    if ($_.Exception.Message -like 'Ollama is already running.*') { throw }
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

Write-Host "Downloading $model (about 5.6 GB)..."
& $ollamaPath pull $model
if ($LASTEXITCODE -ne 0) { throw "Ollama failed to download $model (exit code $LASTEXITCODE)." }

$installed = Invoke-RestMethod -Uri "$api/api/tags"
if (-not ($installed.models | Where-Object { $_.name -like "$model*" })) {
    throw "The model download finished, but $model is not listed by Ollama."
}

Write-Host ''
Write-Host 'Local agent is ready.' -ForegroundColor Green
Write-Host "Model: $model"
Write-Host "API:   $api (loopback only)"
Write-Host 'Open RagdollPet and right-click the cat to choose the local AI chat.'
