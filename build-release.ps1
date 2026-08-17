param(
    [string]$Version = "1.2.0",
    [string]$CertificateThumbprint = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$IncludeScannerTest
)

$ErrorActionPreference = "Stop"
$workspace = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifactRoot = Join-Path $workspace "artifacts"
$packageName = "Better-Movement-for-KBM-GRW"
$portableRoot = Join-Path $artifactRoot "portable\$packageName"
$sourceRoot = Join-Path $artifactRoot "source\$packageName-Source"
$launcherOutput = Join-Path $artifactRoot "publish-launcher"
$runtimeOutput = Join-Path $artifactRoot "publish-runtime"
$scannerRoot = Join-Path $artifactRoot "scanner-test\$packageName"
$scannerLauncherOutput = Join-Path $artifactRoot "publish-launcher-multifile"
$scannerRuntimeOutput = Join-Path $artifactRoot "publish-runtime-multifile"

$buildPaths = @($portableRoot, $sourceRoot, $launcherOutput, $runtimeOutput)
if ($IncludeScannerTest) { $buildPaths += @($scannerRoot, $scannerLauncherOutput, $scannerRuntimeOutput) }
foreach ($path in $buildPaths) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    New-Item -ItemType Directory -Path $path | Out-Null
}

$versionProperties = @("-p:Version=$Version", "-p:InformationalVersion=$Version", "-p:ContinuousIntegrationBuild=true", "-p:DebugType=None", "-p:DebugSymbols=false", "-p:RuntimeIdentifier=win-x64", "-p:SelfContained=false", "-p:PublishSingleFile=true")
dotnet publish (Join-Path $workspace "GRWMovementRuntime\GRWMovementRuntime.csproj") -c Release @versionProperties -o $runtimeOutput
if ($LASTEXITCODE -ne 0) { throw "Runtime publish failed." }

# The runtime is published explicitly above. Avoid republishing the executable
# project reference while producing the framework-dependent single-file launcher.
dotnet publish (Join-Path $workspace "GRWLauncher\GRWLauncher.csproj") -c Release @versionProperties -p:BuildProjectReferences=false -p:SkipRuntimeProjectReference=true -o $launcherOutput
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed." }

if ($CertificateThumbprint) {
    $signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signTool) { throw "SignTool was not found. Install the Windows SDK or omit -CertificateThumbprint." }
    foreach ($binary in @(
        (Join-Path $launcherOutput "Better Movement for KBM - GRW.exe"),
        (Join-Path $runtimeOutput "GRWAnalogueMovement.exe")
    )) {
        & $signTool.Source sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $binary
        if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed for $binary" }
        & $signTool.Source verify /pa /v $binary
        if ($LASTEXITCODE -ne 0) { throw "Authenticode verification failed for $binary" }
    }
}

Copy-Item -LiteralPath (Join-Path $launcherOutput "Better Movement for KBM - GRW.exe") -Destination $portableRoot
New-Item -ItemType Directory -Path (Join-Path $portableRoot "Runtime") -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $runtimeOutput "GRWAnalogueMovement.exe") -Destination (Join-Path $portableRoot "Runtime")
Copy-Item -LiteralPath (Join-Path $workspace "release\README.md") -Destination $portableRoot

$zip = Join-Path $artifactRoot "$packageName-$Version-Portable.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -LiteralPath $portableRoot -DestinationPath $zip -CompressionLevel Optimal

$scannerZip = $null
if ($IncludeScannerTest) {
    $scannerProperties = @("-p:Version=$Version", "-p:InformationalVersion=$Version", "-p:ContinuousIntegrationBuild=true", "-p:DebugType=None", "-p:DebugSymbols=false", "-p:RuntimeIdentifier=win-x64", "-p:SelfContained=false", "-p:PublishSingleFile=false")
    dotnet publish (Join-Path $workspace "GRWMovementRuntime\GRWMovementRuntime.csproj") -c Release @scannerProperties -o $scannerRuntimeOutput
    if ($LASTEXITCODE -ne 0) { throw "Multi-file runtime publish failed." }
    dotnet publish (Join-Path $workspace "GRWLauncher\GRWLauncher.csproj") -c Release @scannerProperties -p:BuildProjectReferences=false -p:SkipRuntimeProjectReference=true -o $scannerLauncherOutput
    if ($LASTEXITCODE -ne 0) { throw "Multi-file launcher publish failed." }

    Copy-Item -LiteralPath (Join-Path $scannerLauncherOutput "Better Movement for KBM - GRW.exe") -Destination $scannerRoot
    Copy-Item -LiteralPath (Join-Path $scannerLauncherOutput "Better Movement for KBM - GRW.dll") -Destination $scannerRoot
    Copy-Item -LiteralPath (Join-Path $scannerLauncherOutput "Better Movement for KBM - GRW.deps.json") -Destination $scannerRoot
    Copy-Item -LiteralPath (Join-Path $scannerLauncherOutput "Better Movement for KBM - GRW.runtimeconfig.json") -Destination $scannerRoot
    $scannerRuntimeRoot = Join-Path $scannerRoot "Runtime"
    New-Item -ItemType Directory -Path $scannerRuntimeRoot -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $scannerRuntimeOutput "GRWAnalogueMovement.exe") -Destination $scannerRuntimeRoot
    Copy-Item -LiteralPath (Join-Path $scannerRuntimeOutput "GRWAnalogueMovement.dll") -Destination $scannerRuntimeRoot
    Copy-Item -LiteralPath (Join-Path $scannerRuntimeOutput "GRWAnalogueMovement.deps.json") -Destination $scannerRuntimeRoot
    Copy-Item -LiteralPath (Join-Path $scannerRuntimeOutput "GRWAnalogueMovement.runtimeconfig.json") -Destination $scannerRuntimeRoot
    Copy-Item -LiteralPath (Join-Path $workspace "release\README.md") -Destination $scannerRoot

    $scannerZip = Join-Path $artifactRoot "$packageName-$Version-Scanner-Test-Multifile.zip"
    if (Test-Path -LiteralPath $scannerZip) { Remove-Item -LiteralPath $scannerZip -Force }
    Compress-Archive -LiteralPath $scannerRoot -DestinationPath $scannerZip -CompressionLevel Optimal
}

foreach ($relative in @("GRWMovementRuntime", "GRWLauncher", "assets\icons\grw.ico", "assets\images\nomad_2.png", "assets\images\better_movement_for_kbm_logo_small_and_trimmed.png", ".gitattributes", ".gitignore", "BetterMovementForKBM.slnx", "README.md", "docs", "CHANGELOG.md", "LICENSE.txt", "release", "build-release.ps1")) {
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
$sourceZip = Join-Path $artifactRoot "$packageName-$Version-Source.zip"
if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip -Force }
Compress-Archive -LiteralPath $sourceRoot -DestinationPath $sourceZip -CompressionLevel Optimal

$releaseHashFile = Join-Path $artifactRoot "RELEASE-SHA256SUMS.txt"
$releaseFiles = @(Get-Item -LiteralPath $zip, $sourceZip)
if ($scannerZip) { $releaseFiles += Get-Item -LiteralPath $scannerZip }
$releaseFiles | Sort-Object Name | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
} | Set-Content -LiteralPath $releaseHashFile -Encoding ascii

Write-Host "Release created: $zip"
