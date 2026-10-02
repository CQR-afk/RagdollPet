param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishPath = Join-Path $repoRoot '.verify\lightweight-publish'
$intermediatePath = (Join-Path $repoRoot '.verify\lightweight-obj') + '\'
$packagePath = Join-Path $repoRoot 'dist\RagdollPet-Lightweight-8GB'

Push-Location $repoRoot
dotnet publish '.\RagdollPet.csproj' `
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

$totalBytes = (Get-ChildItem -LiteralPath $packagePath -File -Recurse | Measure-Object -Property Length -Sum).Sum
Write-Host ''
Write-Host '8 GB VRAM lightweight build created:' -ForegroundColor Green
Write-Host $packagePath
Write-Host ('App package size: {0:N0} MB' -f ($totalBytes / 1MB))
Write-Host 'First run setup-lightweight-agent.ps1 to download qwen3:4b, then launch the pet app.'
