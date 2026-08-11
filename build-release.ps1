param([string]$Version = "0.1.0-beta")

$ErrorActionPreference = "Stop"
$workspace = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifactRoot = Join-Path $workspace "artifacts"
$portableRoot = Join-Path $artifactRoot "portable\GRW-Analogue-Movement-Mod"
$sourceRoot = Join-Path $artifactRoot "source\GRW-Analogue-Movement-Mod-Source"
$runtimeOutput = Join-Path $artifactRoot "publish-runtime"
$safetyOutput = Join-Path $artifactRoot "publish-safety"

foreach ($path in @($portableRoot, $sourceRoot, $runtimeOutput, $safetyOutput)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    New-Item -ItemType Directory -Path $path | Out-Null
}

dotnet publish (Join-Path $workspace "GRWMovementRuntime\GRWMovementRuntime.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $runtimeOutput
if ($LASTEXITCODE -ne 0) { throw "Runtime publish failed." }
dotnet publish (Join-Path $workspace "GRWSafetySetup\GRWSafetySetup.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $safetyOutput
if ($LASTEXITCODE -ne 0) { throw "Safety utility publish failed." }

Copy-Item -LiteralPath (Join-Path $runtimeOutput "GRWAnalogueMovement.exe") -Destination $portableRoot
Copy-Item -LiteralPath (Join-Path $safetyOutput "GRWMovementSafety.exe") -Destination $portableRoot
Copy-Item -LiteralPath (Join-Path $workspace "release\README.md") -Destination $portableRoot
Copy-Item -LiteralPath (Join-Path $workspace "release\DISCLAIMER.txt") -Destination $portableRoot
Copy-Item -LiteralPath (Join-Path $workspace "release\LICENSE.txt") -Destination $portableRoot

$hashFile = Join-Path $portableRoot "SHA256SUMS.txt"
Get-ChildItem -LiteralPath $portableRoot -File | Where-Object Name -ne "SHA256SUMS.txt" | Sort-Object Name | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
} | Set-Content -LiteralPath $hashFile -Encoding ascii

$zip = Join-Path $artifactRoot "GRW-Analogue-Movement-Mod-$Version-Portable.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -LiteralPath $portableRoot -DestinationPath $zip -CompressionLevel Optimal

foreach ($relative in @("GRWSpeedHookPrototype\Program.cs", "GRWSpeedHookPrototype\GRWSpeedHookPrototype.csproj", "GRWMovementRuntime\GRWMovementRuntime.csproj", "GRWSafetySetup\Program.cs", "GRWSafetySetup\GRWSafetySetup.csproj", "installer\GRWAnalogueMovement.iss", "release\README.md", "release\DISCLAIMER.txt", "release\LICENSE.txt", "build-release.ps1")) {
    $source = Join-Path $workspace $relative
    $destination = Join-Path $sourceRoot $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}
$sourceZip = Join-Path $artifactRoot "GRW-Analogue-Movement-Mod-$Version-Source.zip"
if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip -Force }
Compress-Archive -LiteralPath $sourceRoot -DestinationPath $sourceZip -CompressionLevel Optimal

$isccCommand = Get-Command iscc.exe -ErrorAction SilentlyContinue
$isccPath = if ($isccCommand) { $isccCommand.Source } else { Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe" }
if (Test-Path -LiteralPath $isccPath) {
    & $isccPath (Join-Path $workspace "installer\GRWAnalogueMovement.iss")
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }
} else {
    Write-Warning "Inno Setup compiler not found; portable package built and installer definition retained."
}

$releaseHashFile = Join-Path $artifactRoot "RELEASE-SHA256SUMS.txt"
$releaseFiles = @(Get-ChildItem -LiteralPath $artifactRoot -File) + @(Get-ChildItem -LiteralPath (Join-Path $artifactRoot "installer") -File -ErrorAction SilentlyContinue)
$releaseFiles | Where-Object { $_.Name -match '\.(zip|exe)$' } | Sort-Object Name | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
} | Set-Content -LiteralPath $releaseHashFile -Encoding ascii

Write-Host "Release created: $zip"
