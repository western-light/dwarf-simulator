$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\.."
$projectRoot = Resolve-Path "$root\..\..\..\.."
$buildDir = Join-Path $projectRoot "Build\PurrVoiceNativeAudio\Windows-x64"
$outDir = Join-Path $root "Windows"

cmake -S "$PSScriptRoot" -B "$buildDir" -G "Visual Studio 17 2022" -A x64
cmake --build "$buildDir" --config Release

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Copy-Item -Force (Join-Path $buildDir "Release\PurrVoiceNativeAudio.dll") (Join-Path $outDir "PurrVoiceNativeAudio.dll")
