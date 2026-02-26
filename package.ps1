# package.ps1 — Build and package the Pulse Plugin for Vido
# Produces: vido-pulse-1.0.0.zip
# Also deploys to %APPDATA%\Vido\plugins\ for local testing

$ErrorActionPreference = 'Stop'

# Read PluginVersion from Directory.Build.props
$propsPath = Join-Path $PSScriptRoot 'Directory.Build.props'
[xml]$props = Get-Content $propsPath
$version = $props.Project.PropertyGroup.PluginVersion | Where-Object { $_ }
$pluginId   = 'com.vido.pulse'
$projectDir = Join-Path $PSScriptRoot 'src\PulsePlugin'
$publishDir = Join-Path $PSScriptRoot 'publish'
$stageDir   = Join-Path $PSScriptRoot "stage\$pluginId"
$zipName    = "vido-pulse-$version.zip"
$zipPath    = Join-Path $PSScriptRoot $zipName
$deployDir  = Join-Path $env:APPDATA "Vido\plugins\$pluginId"

Write-Host "=== Building Pulse Plugin v$version ===" -ForegroundColor Cyan

# 1. Clean previous artifacts
foreach ($dir in @($publishDir, (Join-Path $PSScriptRoot 'stage'))) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
}
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# 2. Publish the project
Write-Host 'Publishing...'
dotnet publish "$projectDir\PulsePlugin.csproj" -c Release -o $publishDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

# 3. Stage: copy published output into a folder named after the plugin id
Write-Host 'Staging...'
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

# Copy all published files EXCEPT Vido.Core.dll (host already has it)
Get-ChildItem "$publishDir\*" -Recurse |
    Where-Object { $_.Name -ne 'Vido.Core.dll' } |
    ForEach-Object {
        $rel = $_.FullName.Substring($publishDir.Length + 1)
        $dest = Join-Path $stageDir $rel
        $destDir = Split-Path $dest -Parent
        if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
        Copy-Item $_.FullName $dest
    }

# Copy plugin manifest
Copy-Item "$projectDir\plugin.json" $stageDir

# Copy plugin icon from repo root
$iconPath = Join-Path $PSScriptRoot 'Pulse-plugin.png'
if (Test-Path $iconPath) {
    Copy-Item $iconPath $stageDir
}

# Copy Assets folder (icons, etc.)
$assetsDir = Join-Path $PSScriptRoot 'Assets'
if (Test-Path $assetsDir) {
    Copy-Item $assetsDir "$stageDir\Assets" -Recurse
}

# Copy docs if present
foreach ($file in @('README.md', 'CHANGELOG.md')) {
    $src = Join-Path $PSScriptRoot $file
    if (Test-Path $src) { Copy-Item $src $stageDir }
}

# 4. Create zip package
Write-Host "Creating $zipName..."
Compress-Archive -Path (Join-Path $PSScriptRoot 'stage\*') -DestinationPath $zipPath -Force

# 5. Deploy to local Vido plugins directory for testing
Write-Host "Deploying to $deployDir..." -ForegroundColor Yellow
try {
    if (Test-Path $deployDir) { Remove-Item $deployDir -Recurse -Force -ErrorAction Stop }
} catch {
    # Some files may be locked (e.g. by a previous host process).
    # Remove what we can, then overwrite the rest.
    Write-Host "  Warning: could not fully clean deploy dir (locked files). Overwriting..." -ForegroundColor Yellow
    Get-ChildItem $deployDir -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { -not $_.PSIsContainer } |
        ForEach-Object { try { Remove-Item $_.FullName -Force -ErrorAction Stop } catch {} }
}
if (-not (Test-Path $deployDir)) { New-Item $deployDir -ItemType Directory -Force | Out-Null }
Copy-Item (Join-Path $stageDir '*') $deployDir -Recurse -Force

# 6. Copy zip to local test registry
$testRegistryPackages = 'C:\source\testRegistry\packages'
if (Test-Path $testRegistryPackages) {
    Copy-Item $zipPath (Join-Path $testRegistryPackages $zipName) -Force
    Write-Host "Copied to test registry: $testRegistryPackages\$zipName" -ForegroundColor Yellow
}

# 7. Cleanup staging
Remove-Item (Join-Path $PSScriptRoot 'stage') -Recurse -Force

Write-Host ''
Write-Host "Package:  $zipPath" -ForegroundColor Green
Write-Host "Deployed: $deployDir" -ForegroundColor Green
Write-Host 'Restart Vido to load the plugin.' -ForegroundColor Cyan
