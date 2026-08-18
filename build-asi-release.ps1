param(
    [string]$Version = "2.1.0",
    [string]$LoaderPath = ""
)

$ErrorActionPreference = "Stop"
$workspace = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifactRoot = Join-Path $workspace "artifacts"
$packageRoot = Join-Path $artifactRoot "Better-Movement-for-KBM-v$Version"
$zipPath = Join-Path $artifactRoot "Better-Movement-for-KBM-v$Version-ASI.zip"
$project = Join-Path $workspace "BetterMovementASI\BetterMovementASI.vcxproj"
$asi = Join-Path $workspace "BetterMovementASI\bin\Release\x64\BetterMovementForKBM.asi"
$readme = Join-Path $workspace "release\README.txt"
$settings = Join-Path $workspace "release\BetterMovementForKBM.ini"
$expectedLoaderHash = "031A3E5576D91DCE1E438D36B9A3D462C7334AB4791990A8FF1E3DDC0E132DAF"
$loaderDownload = "https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases/download/x64-latest/winmm-x64.zip"
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"

if ([string]::IsNullOrWhiteSpace($LoaderPath)) {
    $LoaderPath = Join-Path $workspace "tools\Ultimate-ASI-Loader-v9.7.4-x64\extracted\dinput8.dll"
}
$loader = [IO.Path]::GetFullPath($LoaderPath)

if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw "Visual Studio Installer's vswhere.exe was not found. Install Visual Studio with the C++ desktop workload."
}
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($msbuild) -or -not (Test-Path -LiteralPath $msbuild -PathType Leaf)) {
    throw "MSBuild was not found. Install Visual Studio with the C++ desktop workload."
}
if (-not (Test-Path -LiteralPath $loader -PathType Leaf)) {
    throw "Ultimate ASI Loader v9.7.4 was not found at $loader. Download the x64 loader from $loaderDownload and pass its extracted DLL with -LoaderPath."
}
if ((Get-FileHash -LiteralPath $loader -Algorithm SHA256).Hash -ne $expectedLoaderHash) {
    throw "Ultimate ASI Loader hash does not match the verified v9.7.4 artifact."
}
& $msbuild $project /t:Rebuild /p:Configuration=Release /p:Platform=x64 /m
if ($LASTEXITCODE -ne 0) { throw "Native ASI build failed." }

$resolvedArtifacts = [IO.Path]::GetFullPath($artifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedPackage = [IO.Path]::GetFullPath($packageRoot)
if (-not $resolvedPackage.StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a package directory outside the artifact root."
}
if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

Copy-Item -LiteralPath $asi -Destination (Join-Path $packageRoot "BetterMovementForKBM.asi")
Copy-Item -LiteralPath $loader -Destination (Join-Path $packageRoot "winmm.dll")
Copy-Item -LiteralPath $readme -Destination (Join-Path $packageRoot "README.txt")
Copy-Item -LiteralPath $settings -Destination (Join-Path $packageRoot "BetterMovementForKBM.ini")

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Nexus package created: $zipPath"
Get-ChildItem -LiteralPath $packageRoot | Select-Object Name, Length
Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
