param([string]$Version = "0.1.0-beta")

$ErrorActionPreference = "Stop"
$workspace = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifactRoot = Join-Path $workspace "artifacts"
$portableRoot = Join-Path $artifactRoot "portable\GRW-Analogue-Movement-Mod"
$sourceRoot = Join-Path $artifactRoot "source\GRW-Analogue-Movement-Mod-Source"
$launcherOutput = Join-Path $artifactRoot "publish-launcher"
$runtimeOutput = Join-Path $launcherOutput "Runtime"

foreach ($path in @($portableRoot, $sourceRoot, $launcherOutput)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    New-Item -ItemType Directory -Path $path | Out-Null
}

dotnet publish (Join-Path $workspace "GRWLauncher\GRWLauncher.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $launcherOutput
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed." }
New-Item -ItemType Directory -Path $runtimeOutput -Force | Out-Null
dotnet publish (Join-Path $workspace "GRWMovementRuntime\GRWMovementRuntime.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $runtimeOutput
if ($LASTEXITCODE -ne 0) { throw "Runtime publish failed." }

Copy-Item -Path (Join-Path $launcherOutput "*") -Destination $portableRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $workspace "release\README.md") -Destination $portableRoot
Copy-Item -LiteralPath (Join-Path $workspace "release\DISCLAIMER.txt") -Destination $portableRoot
Copy-Item -LiteralPath (Join-Path $workspace "release\LICENSE.txt") -Destination $portableRoot

$hashFile = Join-Path $portableRoot "SHA256SUMS.txt"
Get-ChildItem -LiteralPath $portableRoot -File -Recurse | Where-Object Name -ne "SHA256SUMS.txt" | Sort-Object FullName | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $relativeFile = $_.FullName.Substring($portableRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar).Replace('\', '/')
    "$hash  $relativeFile"
} | Set-Content -LiteralPath $hashFile -Encoding ascii

$zip = Join-Path $artifactRoot "GRW-Analogue-Movement-Mod-$Version-Portable.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -LiteralPath $portableRoot -DestinationPath $zip -CompressionLevel Optimal

foreach ($relative in @("GRWSpeedHookPrototype\Program.cs", "GRWSpeedHookPrototype\GRWSpeedHookPrototype.csproj", "GRWMovementRuntime\GRWMovementRuntime.csproj", "GRWLauncher", "installer\GRWAnalogueMovement.iss", "release\README.md", "release\DISCLAIMER.txt", "release\LICENSE.txt", "build-release.ps1")) {
    $source = Join-Path $workspace $relative
    $destination = Join-Path $sourceRoot $relative
    if (Test-Path -LiteralPath $source -PathType Container) {
        Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]' | ForEach-Object {
            $relativeFile = $_.FullName.Substring($source.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
            $destinationFile = Join-Path $destination $relativeFile
            New-Item -ItemType Directory -Path (Split-Path -Parent $destinationFile) -Force | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $destinationFile
        }
    } else {
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
    }
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
