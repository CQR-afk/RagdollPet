$ErrorActionPreference = 'Stop'
$model = 'qwen3:4b'
$repoRoot = Split-Path -Parent $PSScriptRoot
$modelRoot = Join-Path $repoRoot 'OllamaModels'
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
    if (-not $ollamaPath) { throw 'Ollama was not found. Install Ollama and run this script again.' }
} else {
    $ollamaPath = $ollama.Source
}

$api = 'http://127.0.0.1:11434'
try {
    $null = Invoke-RestMethod -Uri "$api/api/tags" -TimeoutSec 2
    throw "Ollama is already running. Quit Ollama from the system tray, then rerun this script so the model is downloaded to $modelRoot. No model was downloaded."
}
catch {
    if ($_.Exception.Message -like 'Ollama is already running.*') { throw }
    Write-Host 'Starting local Ollama...'
    Start-Process -FilePath $ollamaPath -ArgumentList 'serve' -WindowStyle Hidden
    $ready = $false
    for ($i = 0; $i -lt 45; $i++) {
        Start-Sleep -Seconds 1
        try { $null = Invoke-RestMethod -Uri "$api/api/tags" -TimeoutSec 2; $ready = $true; break }
        catch { }
    }
    if (-not $ready) { throw 'Ollama did not start on 127.0.0.1:11434.' }
}

Write-Host 'Downloading Qwen3 4B (model files are about 2.5 GB)...'
& $ollamaPath pull $model
if ($LASTEXITCODE -ne 0) { throw "Ollama failed to download $model (exit code $LASTEXITCODE)." }
$installed = Invoke-RestMethod -Uri "$api/api/tags"
if (-not ($installed.models | Where-Object { $_.name -like 'qwen3:4b*' })) {
    throw 'The download ended, but Ollama does not list qwen3:4b.'
}

Write-Host ''
Write-Host 'Lightweight model is ready.' -ForegroundColor Green
Write-Host 'Default model: qwen3:4b (about 2.5 GB, suitable for the 8 GB VRAM tier).'
Write-Host "Model directory: $modelRoot"
Write-Host 'Existing models are not removed or replaced.'
