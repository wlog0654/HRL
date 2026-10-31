$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $projectRoot 'build'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /reference:System.Drawing.dll "/out:$outputRoot\IconBuilder.exe" "$PSScriptRoot\IconBuilder.cs"
if ($LASTEXITCODE -ne 0) { throw 'Icon compiler failed' }
& "$outputRoot\IconBuilder.exe" "$projectRoot\assets"
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed' }
