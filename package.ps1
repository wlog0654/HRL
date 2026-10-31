param([switch]$SkipDesktopTests)
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\build.ps1" -SkipDesktopTests:$SkipDesktopTests
$exePath = Join-Path $PSScriptRoot 'build\HearthstoneReconnectLite.exe'
$version = (Get-Item -LiteralPath $exePath).VersionInfo.FileVersion
$releaseName = "HearthstoneReconnectLite-$version-win-x64"
$distRoot = Join-Path $PSScriptRoot 'dist'
$stagingRoot = Join-Path (Join-Path (Join-Path $PSScriptRoot 'build') ('package-' + [Guid]::NewGuid().ToString('N'))) $releaseName
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
# Explicit file allowlist; never package test executables or diagnostic output.
foreach ($filePath in @($exePath, "$PSScriptRoot\README.md", "$PSScriptRoot\LICENSE", "$PSScriptRoot\CHANGELOG.md", "$PSScriptRoot\CONTRIBUTING.md", "$PSScriptRoot\SECURITY.md")) {
    Copy-Item -LiteralPath $filePath -Destination $stagingRoot -Force
}
foreach ($subdirectory in @('docs', 'assets')) { New-Item -ItemType Directory -Path (Join-Path $stagingRoot $subdirectory) -Force | Out-Null }
Copy-Item -LiteralPath "$PSScriptRoot\docs\architecture.md" -Destination "$stagingRoot\docs"
Copy-Item -LiteralPath "$PSScriptRoot\assets\icon.png" -Destination "$stagingRoot\assets"
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
$zipPath = Join-Path $distRoot "$releaseName.zip"
Compress-Archive -LiteralPath $stagingRoot -DestinationPath $zipPath -Force
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $distRoot 'SHA256SUMS.txt'), "$hash  $releaseName.zip`n", [Text.Encoding]::ASCII)
Write-Host "Package: $zipPath"
