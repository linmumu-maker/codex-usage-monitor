[CmdletBinding()]
param([switch]$Package)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$taskCompiler = Join-Path $taskFramework 'csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'Requires Windows x64 with .NET Framework 4.8.' }
$taskOutput = Join-Path $taskRoot 'artifacts\CodexUsageMonitor'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $taskRoot 'artifacts\tests') -Force | Out-Null
$taskReferences = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Xaml.dll')
$taskArguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/warn:4',"/win32manifest:$taskRoot\app.manifest","/out:$taskOutput\CodexUsageMonitor.exe")
foreach ($taskRef in $taskReferences) { $taskArguments += "/reference:$taskFramework\$taskRef" }
foreach ($taskRef in @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','UIAutomationClient.dll','UIAutomationTypes.dll')) { $taskArguments += "/reference:$taskFramework\WPF\$taskRef" }
$taskSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $taskCompiler @taskArguments @taskSources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'CodexUsageMonitor.exe.config') -Destination $taskOutput
if (-not (Test-Path -LiteralPath (Join-Path $taskOutput 'settings.json'))) {
    Copy-Item -LiteralPath (Join-Path $taskRoot 'config\settings.default.json') -Destination (Join-Path $taskOutput 'settings.json')
}
$taskTestArgs = $taskArguments | Where-Object { $_ -notlike '/out:*' -and $_ -ne '/target:winexe' }
$taskTestArgs += @('/target:exe','/main:CodexUsageMonitor.Tests.TestRunner',"/out:$taskRoot\artifacts\tests\MonitorTests.exe")
$taskTestSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'tests') -Filter '*.cs' | ForEach-Object FullName)
& $taskCompiler @taskTestArgs @taskSources @taskTestSources
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'CodexUsageMonitor.exe.config') -Destination (Join-Path $taskRoot 'artifacts\tests\MonitorTests.exe.config')
foreach ($taskName in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $taskRoot $taskName) -Destination $taskOutput }
New-Item -ItemType Directory -Path (Join-Path $taskOutput 'docs') -Force | Out-Null
Copy-Item -Path (Join-Path $taskRoot 'docs\*.md') -Destination (Join-Path $taskOutput 'docs')
$taskHash = Get-FileHash -LiteralPath (Join-Path $taskOutput 'CodexUsageMonitor.exe') -Algorithm SHA256
$taskManifest = Join-Path $taskOutput 'SHA256SUMS.txt'
$taskManifestTemp = Join-Path $taskOutput ('SHA256SUMS-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::WriteAllText($taskManifestTemp, "$($taskHash.Hash.ToLowerInvariant())  CodexUsageMonitor.exe" + [Environment]::NewLine, [Text.Encoding]::ASCII)
    # Atomic replacement avoids truncating a manifest temporarily mapped by Windows readers.
    if (Test-Path -LiteralPath $taskManifest) { [IO.File]::Replace($taskManifestTemp, $taskManifest, [NullString]::Value) }
    else { [IO.File]::Move($taskManifestTemp, $taskManifest) }
} finally {
    if (Test-Path -LiteralPath $taskManifestTemp) { Remove-Item -LiteralPath $taskManifestTemp -Force }
}
if ($Package) {
    & (Join-Path $PSScriptRoot 'package.ps1')
}
Write-Output "Built: $taskOutput\CodexUsageMonitor.exe"
