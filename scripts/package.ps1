[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskOutput = Join-Path $taskRoot 'artifacts\CodexUsageMonitor'
$taskExe = Join-Path $taskOutput 'CodexUsageMonitor.exe'
if (-not (Test-Path -LiteralPath $taskExe)) { throw 'Build first with scripts/build.ps1.' }
foreach ($taskName in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $taskRoot $taskName) -Destination $taskOutput }
Copy-Item -Path (Join-Path $taskRoot 'docs\*.md') -Destination (Join-Path $taskOutput 'docs')
$taskHash = Get-FileHash -LiteralPath $taskExe -Algorithm SHA256
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
$taskZip = Join-Path $taskRoot 'artifacts\CodexUsageMonitor-1.0.0-windows-x64.zip'
$taskStage = [IO.Path]::GetFullPath((Join-Path $taskRoot ('work\package-' + [Guid]::NewGuid().ToString('N'))))
$taskStageBoundary = [IO.Path]::GetFullPath((Join-Path $taskRoot 'work')) + [IO.Path]::DirectorySeparatorChar
if (-not $taskStage.StartsWith($taskStageBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Staging must stay inside project work directory.' }
New-Item -ItemType Directory -Path $taskStage | Out-Null
try {
    $taskStageOutput = Join-Path $taskStage 'CodexUsageMonitor'
    Copy-Item -LiteralPath $taskOutput -Destination $taskStageOutput -Recurse
    # Share a clean default config, never the current machine's CLI path or calibrated region.
    Copy-Item -LiteralPath (Join-Path $taskRoot 'config\settings.default.json') -Destination (Join-Path $taskStageOutput 'settings.json') -Force
    $taskStageZip = Join-Path $taskStage 'package.zip'
    Add-Type -AssemblyName System.IO.Compression
    $taskArchiveStream = [IO.File]::Open($taskStageZip, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $taskArchive = [IO.Compression.ZipArchive]::new($taskArchiveStream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($taskFile in (Get-ChildItem -LiteralPath $taskStageOutput -Recurse -File)) {
            $taskEntryName = $taskFile.FullName.Substring($taskStage.Length + 1).Replace('\', '/')
            $taskEntry = $taskArchive.CreateEntry($taskEntryName, [IO.Compression.CompressionLevel]::Optimal)
            $taskInput = $null; $taskEntryStream = $null
            try {
                # Read-only shared access tolerates readers and writers holding document handles.
                $taskInput = [IO.File]::Open($taskFile.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read,
                    ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
                $taskEntryStream = $taskEntry.Open()
                $taskInput.CopyTo($taskEntryStream)
            } finally {
                if ($taskEntryStream) { $taskEntryStream.Dispose() }
                if ($taskInput) { $taskInput.Dispose() }
            }
        }
    } finally {
        $taskArchive.Dispose()
        $taskArchiveStream.Dispose()
    }
    if (Test-Path -LiteralPath $taskZip) { [IO.File]::Replace($taskStageZip, $taskZip, [NullString]::Value) }
    else { [IO.File]::Move($taskStageZip, $taskZip) }
} finally {
    # This resolved, newly created GUID directory was checked against the project work boundary above.
    if (Test-Path -LiteralPath $taskStage) { Remove-Item -LiteralPath $taskStage -Recurse -Force }
}
Write-Output "Packaged: $taskZip"
