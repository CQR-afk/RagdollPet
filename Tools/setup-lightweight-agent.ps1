$ErrorActionPreference = 'Stop'
$model = 'qwen3:4b'
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
try { $null = Invoke-RestMethod -Uri "$api/api/tags" -TimeoutSec 2 }
catch {
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
Write-Host 'The model is stored in the current Ollama model directory; existing models are not removed or replaced.'
