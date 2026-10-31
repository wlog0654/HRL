param([switch]$SkipDesktopTests)
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$projectRoot = $PSScriptRoot
$outputDirectory = Join-Path $projectRoot 'build'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$uiSources = @('Hotkey.cs', 'HotkeyCapture.cs', 'HotkeyDialog.cs', 'OverlayInteraction.cs', 'LayeredSurface.cs', 'DesktopUi.cs', 'NativeContextMenu.cs', 'Branding.cs', 'FloatingButton.cs') | ForEach-Object { Join-Path "$projectRoot\src" $_ }
$connectionSources = @("$projectRoot\src\ConnectionSelector.cs", "$projectRoot\src\NativeTcp.cs")
$appSources = $connectionSources + $uiSources + @("$projectRoot\src\Program.cs", "$projectRoot\src\AssemblyInfo.cs")
$desktopResources = @("/win32icon:$projectRoot\assets\AppIcon.ico", "/resource:$projectRoot\assets\AppIcon.ico,HsReconnectLite.AppIcon.ico", "/win32manifest:$projectRoot\src\app.manifest")
& $compilerPath /nologo /optimize+ /target:winexe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$outputDirectory\HRL.exe" @desktopResources @appSources
if ($LASTEXITCODE -ne 0) { throw 'App build failed' }
$testSpecs = @(
    @{Name='HotkeyTests'; Sources=$appSources},
    @{Name='HotkeyCaptureTests'; Sources=$uiSources},
    @{Name='SelectionTests'; Sources=@("$projectRoot\src\ConnectionSelector.cs")},
    @{Name='NativeTests'; Sources=$connectionSources},
    @{Name='OverlayTests'; Sources=@("$projectRoot\src\OverlayInteraction.cs", "$projectRoot\src\Hotkey.cs")},
    @{Name='OverlayWindowTests'; Sources=$uiSources},
    @{Name='TrayIntegrationTests'; Sources=$appSources},
    @{Name='DesktopUiTests'; Sources=$uiSources},
    @{Name='MenuVisualTests'; Sources=$uiSources}
)
foreach ($testSpec in $testSpecs) {
    $testName = $testSpec.Name
    if ($SkipDesktopTests -and $testName -in @('HotkeyCaptureTests', 'OverlayWindowTests', 'TrayIntegrationTests', 'MenuVisualTests')) {
        Write-Host "SKIP $testName (interactive desktop required)"
        continue
    }
    $testSources = $testSpec.Sources + @("$projectRoot\tests\$testName.cs")
    & $compilerPath /nologo /platform:x64 "/main:$testName" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/out:$outputDirectory\$testName.exe" @desktopResources @testSources
    if ($LASTEXITCODE -ne 0) { throw "$testName build failed" }
    & "$outputDirectory\$testName.exe"
    if ($LASTEXITCODE -ne 0) { throw "$testName failed" }
}
