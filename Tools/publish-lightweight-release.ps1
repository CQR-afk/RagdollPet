param(
    [string]$Tag = 'v1.19',
    [string]$AssetPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\RagdollPet-Lightweight-8GB-v1.19.zip')
)

$ErrorActionPreference = 'Stop'
$repo = 'CQR-afk/RagdollPet'
if (-not (Test-Path -LiteralPath $AssetPath)) { throw "Release asset not found: $AssetPath" }
$asset = Get-Item -LiteralPath $AssetPath
if ($asset.Length -ge 2GB) { throw 'GitHub release assets must be smaller than 2 GiB.' }

$env:GCM_INTERACTIVE = 'never'
$credentialInput = "protocol=https`nhost=github.com`n`n"
$credentialLines = @($credentialInput | git credential fill 2>$null)
$tokenLine = $credentialLines | Where-Object { $_ -like 'password=*' } | Select-Object -First 1
$token = if ($tokenLine) { $tokenLine.Substring('password='.Length) } else { $null }
if ([string]::IsNullOrWhiteSpace($token)) { throw 'No cached GitHub credential is available.' }

$headers = @{
    Authorization = "Bearer $token"
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
}
$release = $null
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/tags/$Tag" -Headers $headers -Method Get -TimeoutSec 30
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    if ($statusCode -ne 404) { throw }
}

if ($null -eq $release) {
    $releaseBody = @{
        tag_name = $Tag
        target_commitish = 'main'
        name = "RagdollPet $Tag - Lightweight Edition (8GB VRAM)"
        body = @'
**8GB VRAM LIGHTWEIGHT EDITION**

- Standalone Windows x64 app package; about 223 MB.
- Uses Qwen3 4B by default. Run `setup-lightweight-agent.ps1` once to download the approximately 2.5 GB Ollama model.
- Ollama must be installed separately. Existing Ollama models are not removed or replaced.
- Launch `团团2.5D桌宠.exe` after setup.
'@
        draft = $false
        prerelease = $false
    } | ConvertTo-Json -Depth 5
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases" -Headers $headers -Method Post -Body $releaseBody -ContentType 'application/json' -TimeoutSec 60
    Write-Host "Created release $($release.tag_name)." -ForegroundColor Green
}

$existingAsset = @($release.assets | Where-Object { $_.name -eq $asset.Name }) | Select-Object -First 1
if ($existingAsset) { throw "Asset already exists on this release: $($asset.Name). Remove it in GitHub first if you intend to replace it." }
$uploadBase = $release.upload_url -replace '\{\?name,label\}$', ''
Add-Type -AssemblyName System.Net.Http
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromHours(2)
$client.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
$client.DefaultRequestHeaders.Accept.ParseAdd('application/vnd.github+json')
$client.DefaultRequestHeaders.UserAgent.ParseAdd('RagdollPet-Lightweight-ReleaseUploader/1.0')
try {
    $stream = [IO.File]::Open($asset.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $content = [System.Net.Http.StreamContent]::new($stream, 8MB)
    $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new('application/octet-stream')
    $uri = $uploadBase + '?name=' + [Uri]::EscapeDataString($asset.Name)
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, $uri)
    $request.Content = $content
    try {
        Write-Host "Uploading $($asset.Name) ($([math]::Round($asset.Length / 1MB)) MB)..."
        $response = $client.SendAsync($request, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        try {
            if (-not $response.IsSuccessStatusCode) { throw "GitHub upload failed: HTTP $([int]$response.StatusCode)." }
            $uploaded = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
            Write-Host "Uploaded $($uploaded.name) ($($uploaded.size) bytes)." -ForegroundColor Green
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
} finally {
    $client.Dispose()
    $token = $null
    $credentialLines = $null
    $credentialInput = $null
}

Write-Host $release.html_url
