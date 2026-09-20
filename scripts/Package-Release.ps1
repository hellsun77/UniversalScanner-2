param(
    [string]$Configuration = "Release",
    [string]$Platform = "AnyCPU"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "UniversalScanner\UniversalScanner.csproj"
$projectDir = Join-Path $root "UniversalScanner"
if ($Platform -eq "AnyCPU") {
    $outputDir = Join-Path $projectDir "bin\$Configuration"
} else {
    $outputDir = Join-Path $projectDir "bin\$Platform\$Configuration"
}
$sdkDir = Join-Path $projectDir "SDK"
$hikvisionSdkDir = Join-Path $sdkDir "Hikvision"
$packageRoot = Join-Path $root "dist"
$packageDir = Join-Path $packageRoot "UniversalScanner-Release"
$zipPath = Join-Path $packageRoot "UniversalScanner-Release.zip"

$msbuildCandidates = @(
    "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
)

$msbuild = $msbuildCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $msbuild) {
    throw "MSBuild was not found. Install Visual Studio Build Tools 2022 with .NET Framework 4.8 targeting pack."
}

Write-Host "Building $Configuration|$Platform..."
& $msbuild $project "/p:Configuration=$Configuration" "/p:Platform=$Platform" /m
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $packageRoot)) {
    New-Item -ItemType Directory -Path $packageRoot | Out-Null
}

if (Test-Path $packageDir) {
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}

New-Item -ItemType Directory -Path $packageDir | Out-Null

Write-Host "Copying app files..."
Copy-Item -LiteralPath (Join-Path $outputDir "UniversalScanner.exe") -Destination $packageDir
Copy-Item -LiteralPath (Join-Path $outputDir "UniversalScanner.exe.config") -Destination $packageDir -ErrorAction SilentlyContinue

Get-ChildItem -LiteralPath $outputDir -Filter "*.selftest" -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $packageDir
}

$iss = Join-Path $outputDir "UniversalScanner.iss"
if (Test-Path $iss) {
    Copy-Item -LiteralPath $iss -Destination $packageDir
}

Write-Host "Copying native SDK files beside the exe..."
Get-ChildItem -LiteralPath $hikvisionSdkDir -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $packageDir
}

$hikvisionComDir = Join-Path $hikvisionSdkDir "HCNetSDKCom"
if (Test-Path $hikvisionComDir) {
    Copy-Item -LiteralPath $hikvisionComDir -Destination (Join-Path $packageDir "HCNetSDKCom") -Recurse
}

Write-Host "Copying full SDK folder..."
Copy-Item -LiteralPath $sdkDir -Destination (Join-Path $packageDir "SDK") -Recurse

$readmePath = Join-Path $packageDir "README.txt"
@(
    "Universal Scanner Release Package",
    "",
    "Run UniversalScanner.exe from this folder.",
    "",
    "Keep the DLL files, HCNetSDKCom folder, and SDK folder beside UniversalScanner.exe.",
    "They are required by the Hikvision SDK and future vendor SDK additions.",
    "",
    "To rebuild this package after future code changes, run:",
    "powershell -ExecutionPolicy Bypass -File scripts\Package-Release.ps1"
) | Set-Content -LiteralPath $readmePath -Encoding ASCII

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Write-Host "Creating zip..."
Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "Package folder: $packageDir"
Write-Host "Package zip:    $zipPath"
