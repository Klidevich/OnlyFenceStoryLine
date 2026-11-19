#requires -Version 7.2
[CmdletBinding(DefaultParameterSetName = 'Archive')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Archive')][string]$ReleaseArchive,
    [Parameter(Mandatory, ParameterSetName = 'Directory')][string]$ContentDirectory,
    [Parameter(Mandatory)][string]$GamePath,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $root 'build-manifest.json') -Raw | ConvertFrom-Json -AsHashtable
if ($manifest.format -ne 1) { throw 'Unsupported build manifest format.' }
$build = Join-Path $root '.build'
# A fresh tree on every invocation prevents removed or renamed source files surviving a rebuild.
$prepared = Join-Path $build ('run-' + [Guid]::NewGuid().ToString('N'))
$content = Join-Path $prepared 'Content'
New-Item -ItemType Directory -Path $content -Force | Out-Null
$archive = $null
try {
    if ($PSCmdlet.ParameterSetName -eq 'Archive') {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ReleaseArchive))
    }
    foreach ($name in $manifest.content.Keys) {
        if ($name -notmatch '^[a-z0-9-]+\.json$') { throw "Invalid content filename: $name" }
        $destination = Join-Path $content $name
        if ($archive) {
            $entryName = $manifest.archivePrefix + $name
            $entries = @($archive.Entries | Where-Object FullName -CEQ $entryName)
            if ($entries.Count -ne 1 -or $entries[0].Length -gt 16MB) { throw "Missing, duplicate or oversized release content: $entryName" }
            $inputStream = $entries[0].Open()
            $outputStream = [IO.File]::Create($destination)
            try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
        } else {
            Copy-Item -LiteralPath (Join-Path $ContentDirectory $name) -Destination $destination
        }
        $actual = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -cne $manifest.content[$name]) { throw "Release content hash mismatch: $name. Use the matching release ZIP." }
    }
} finally { if ($archive) { $archive.Dispose() } }
$catalog = Get-Content -LiteralPath (Join-Path $content 'source-content.json') -Raw | ConvertFrom-Json -AsHashtable
if ($catalog.format -ne 1 -or $catalog.version -cne $manifest.version) { throw 'Release content format/version mismatch.' }
$marker = '/\* release-text:([A-Za-z0-9_.]+) \*/ ""'
$matched = 0
$modified = [Collections.Generic.List[string]]::new()
foreach ($project in @('Client', 'Server')) {
    $target = Join-Path $prepared $project
    New-Item -ItemType Directory -Path $target | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root $project) -File | Where-Object Extension -In @('.cs', '.csproj')) {
        $text = [IO.File]::ReadAllText($file.FullName)
        $relative = "$project/$($file.Name)"
        $bom = $false
        if ($file.Extension -eq '.cs') {
            $text = [regex]::Replace($text, $marker, [Text.RegularExpressions.MatchEvaluator]{ param($m)
                $key = $m.Groups[1].Value
                if (!$catalog.tokens.ContainsKey($key)) { throw "Missing release text: $key" }
                return [string]$catalog.tokens[$key]
            })
            if ($text.Contains('/* release-text:')) { throw "Unresolved release text in $relative" }
            if ($manifest.sources.ContainsKey($relative)) { $bom = [bool]$manifest.sources[$relative].utf8Bom }
        }
        $destination = Join-Path $target $file.Name
        [IO.File]::WriteAllText($destination, $text, [Text.UTF8Encoding]::new($bom))
        if ($file.Extension -eq '.cs') {
            $actual = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($manifest.sources.ContainsKey($relative) -and $actual -ceq $manifest.sources[$relative].sha256) { $matched++ }
            else { $modified.Add($relative) }
        }
    }
}
$missing = @($manifest.sources.Keys | Where-Object { !(Test-Path -LiteralPath (Join-Path $prepared $_)) })
foreach ($name in $missing) { $modified.Add($name) }
if ($modified.Count) { Write-Warning "Building modified sources: $($modified -join ', ')" }
$report = [ordered]@{ version = $manifest.version; preparedDirectory = $prepared; matchingReleaseSources = $matched; modifiedSources = @($modified); built = $false }
if (!$PrepareOnly) {
    $game = [IO.Path]::GetFullPath($GamePath)
    $client = Join-Path $prepared 'Client/E17.Client.csproj'
    $server = Join-Path $prepared 'Server/E17.Server.csproj'
    & dotnet build $client -c Release "-p:GamePath=$game" '-p:ReleaseContentPrepared=true' "-p:PathMap=$prepared=/_/OnlyFenceStoryLine" '-p:ContinuousIntegrationBuild=true' '-p:DebugType=none' '-p:IncludeSourceRevisionInInformationalVersion=false'
    if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' }
    & dotnet build $server -c Release "-p:SptRuntime=$(Join-Path $game 'SPT_Runtime')" '-p:ReleaseContentPrepared=true' "-p:PathMap=$prepared=/_/OnlyFenceStoryLine" '-p:ContinuousIntegrationBuild=true' '-p:DebugType=none' '-p:IncludeSourceRevisionInInformationalVersion=false'
    if ($LASTEXITCODE -ne 0) { throw 'Server build failed.' }
    $report.built = $true
    $report.client = Join-Path $prepared 'Client/bin/Release/net472/CustomQuests.E17.dll'
    $report.server = Join-Path $prepared 'Server/bin/Release/net10.0/CustomQuests.E17.Server.dll'
    $report.clientSha256 = (Get-FileHash -LiteralPath $report.client -Algorithm SHA256).Hash.ToLowerInvariant()
    $report.serverSha256 = (Get-FileHash -LiteralPath $report.server -Algorithm SHA256).Hash.ToLowerInvariant()
    $report.sdk = (& dotnet --version | Out-String).Trim()
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $build 'last-build.json') -Encoding utf8
$report | ConvertTo-Json -Depth 10
