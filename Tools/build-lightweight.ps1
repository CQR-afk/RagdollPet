param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$taskDotnetHome = Join-Path $repoRoot '.tools\dotnet-home'
$taskNugetPackages = Join-Path $repoRoot '.nuget\packages'
$env:DOTNET_CLI_HOME = $taskDotnetHome
$env:NUGET_PACKAGES = $taskNugetPackages
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$publishPath = Join-Path $repoRoot '.verify\lightweight-publish'
$intermediatePath = (Join-Path $repoRoot '.verify\lightweight-obj') + '\'
$packagePath = Join-Path $repoRoot 'dist\RagdollPet-Lightweight-8GB'

if (Get-Process -Name '团团2.5D桌宠' -ErrorAction SilentlyContinue) {
    throw '请先正常退出轻量版桌宠，再更新发布目录。'
}

Push-Location $repoRoot
& $dotnetCommand publish '.\RagdollPet.csproj' `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishReadyToRun=false `
    '-p:BaseIntermediateOutputPath=.verify\lightweight-obj/' `
    '-p:MSBuildProjectExtensionsPath=.verify\lightweight-obj/' `
    -o '.verify\lightweight-publish'
$publishExitCode = $LASTEXITCODE
Pop-Location
if ($publishExitCode -ne 0) { throw 'Pet publish failed.' }

New-Item -ItemType Directory -Path $packagePath -Force | Out-Null
Copy-Item -Path (Join-Path $publishPath '*') -Destination $packagePath -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'lightweight-model.txt') `
    -Destination (Join-Path $packagePath 'preferred-model.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'setup-lightweight-agent.ps1') `
    -Destination (Join-Path $packagePath 'setup-lightweight-agent.ps1') -Force
$packageTools = Join-Path $packagePath 'Tools'
New-Item -ItemType Directory -Path $packageTools -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'start-searxng.ps1') -Destination $packageTools -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'stop-searxng.ps1') -Destination $packageTools -Force
$packageSearxng = Join-Path $packageTools 'searxng'
New-Item -ItemType Directory -Path $packageSearxng -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'searxng\compose.yaml') -Destination $packageSearxng -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'searxng\settings.yml') -Destination $packageSearxng -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'searxng\README.md') -Destination $packageSearxng -Force
$playwrightSource = Join-Path $taskNugetPackages 'microsoft.playwright\1.63.0\.playwright'
$playwrightTarget = Join-Path $packagePath '.playwright'
$playwrightNode = Join-Path $playwrightSource 'node\win32_x64\node.exe'
if (-not (Test-Path -LiteralPath $playwrightNode)) {
    throw "Microsoft.Playwright runtime files are missing from the D: package cache: $playwrightSource"
}
$playwrightNodeTarget = Join-Path $playwrightTarget 'node\win32_x64'
$playwrightPackageTarget = Join-Path $playwrightTarget 'package'
New-Item -ItemType Directory -Path $playwrightNodeTarget -Force | Out-Null
New-Item -ItemType Directory -Path $playwrightPackageTarget -Force | Out-Null
Copy-Item -LiteralPath $playwrightNode -Destination $playwrightNodeTarget -Force
Copy-Item -LiteralPath (Join-Path $playwrightSource 'node\LICENSE') -Destination (Join-Path $playwrightTarget 'node') -Force
Copy-Item -Path (Join-Path $playwrightSource 'package\*') -Destination $playwrightPackageTarget -Recurse -Force
Copy-Item -LiteralPath (Join-Path $taskNugetPackages 'microsoft.playwright\1.63.0\buildTransitive\playwright.ps1') `
    -Destination (Join-Path $packagePath 'playwright.ps1') -Force

$totalBytes = (Get-ChildItem -LiteralPath $packagePath -File -Recurse | Measure-Object -Property Length -Sum).Sum
Write-Host ''
Write-Host '8 GB VRAM lightweight build created:' -ForegroundColor Green
Write-Host $packagePath
Write-Host ('App package size: {0:N0} MB' -f ($totalBytes / 1MB))
Write-Host 'First run setup-lightweight-agent.ps1 to download qwen3:4b, then launch the pet app.'
