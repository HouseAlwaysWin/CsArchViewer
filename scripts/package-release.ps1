param(
    [Parameter(Mandatory = $true)]
    [string]$VersionTag
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/CsArchViewer.Avalonia/CsArchViewer.Avalonia.csproj"
$publishDir = Join-Path $repoRoot "artifacts/publish/win-x64"
$packageDir = Join-Path $repoRoot "artifacts/package"
$zipName = "CsArchViewer-$VersionTag-win-x64.zip"
$zipPath = Join-Path $packageDir $zipName

Write-Host "[release] Version: $VersionTag"
Write-Host "[release] Project: $projectPath"

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

dotnet restore $projectPath
dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:PublishTrimmed=false `
    -o $publishDir

if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

Write-Host "[release] Package created: $zipPath"

if ($env:GITHUB_OUTPUT) {
    "zip_path=$zipPath" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "zip_name=$zipName" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}
param(
    [Parameter(Mandatory = $true)]
    [string]$VersionTag
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/CsArchViewer.Avalonia/CsArchViewer.Avalonia.csproj"
$publishDir = Join-Path $root "artifacts/publish/win-x64"
$packageDir = Join-Path $root "artifacts/package"
$zipName = "CsArchViewer-$VersionTag-win-x64.zip"
$zipPath = Join-Path $packageDir $zipName

Write-Host "Packaging version: $VersionTag"
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

dotnet restore $project
dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:PublishTrimmed=false `
    -o $publishDir

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

Write-Host "Package created: $zipPath"
Write-Host "##vso[task.setvariable variable=ZIP_PATH]$zipPath"
