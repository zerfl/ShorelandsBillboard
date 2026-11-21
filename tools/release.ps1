<#
.SYNOPSIS
Builds dist\ShorelandsBillboard-<version>.zip, the package that is unzipped into the game folder.

.DESCRIPTION
Downloads the pinned libVLC and LibVLCSharp packages from nuget.org (SHA-256 checked, kept in .cache\),
runs the tests, builds the plugin, checks what goes into the package and writes the zip. Nothing is uploaded.

.PARAMETER GameDir
The folder that holds Shorelands.exe, with BepInEx installed. Default: the SHORELANDS_DIR environment variable.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\release.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\Shorelands Playtest"
#>
param([string]$GameDir = $env:SHORELANDS_DIR)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-StrictMode -Version 2

$packages = @(
    @{ Id = 'VideoLAN.LibVLC.Windows'; Version = '3.0.24'; Sha256 = '3da3f37ee2b2b10e5c788d830a3b513658c36521ea1b0f966ed86eb69f41ef3c' },
    @{ Id = 'LibVLCSharp'; Version = '3.10.2'; Sha256 = 'a7473e6d7c3595c74d3fb42bb0791053224d07b797adcee8c16f00e6f99bb8f9' }
)
# The libVLC modules that ship (plugins\<folder>\lib<name>_plugin.dll): the callback input, MPEG-TS, H.264 and
# AAC through libavcodec, picture and sound into memory, and the converters between them. All are LGPL.
$modules = @(
    'access/imem', 'stream_filter/prefetch', 'stream_filter/cache_read', 'demux/ts',
    'packetizer/packetizer_h264', 'packetizer/packetizer_mpeg4audio', 'codec/avcodec',
    'audio_output/amem', 'audio_mixer/integer_mixer', 'audio_mixer/float_mixer', 'audio_filter/audio_format',
    'audio_filter/speex_resampler', 'audio_filter/ugly_resampler', 'audio_filter/trivial_channel_mixer',
    'audio_filter/simple_channel_mixer', 'video_output/vmem', 'video_chroma/swscale', 'text_renderer/tdummy'
)

$root = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $root '.cache'
$dist = Join-Path $root 'dist'
$pluginPath = 'BepInEx/plugins/ShorelandsBillboard'

function Fail([string]$message) { throw "release: $message" }

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { Fail "dotnet $($args[0]) failed" }
}

function Get-Package($package) {
    $id = $package.Id.ToLowerInvariant()
    $file = Join-Path $cache "$id.$($package.Version).nupkg"
    if (-not (Test-Path $file) -or (Get-FileHash $file -Algorithm SHA256).Hash -ne $package.Sha256) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -OutFile $file "https://api.nuget.org/v3-flatcontainer/$id/$($package.Version)/$id.$($package.Version).nupkg"
        if ((Get-FileHash $file -Algorithm SHA256).Hash -ne $package.Sha256) {
            Remove-Item $file
            Fail "checksum mismatch for $($package.Id) $($package.Version)"
        }
    }
    $file
}

# Copies one entry of an archive to a file; nothing in the archive is run.
function Export-Entry($archive, [string]$name, [string]$target) {
    $entry = $archive.GetEntry($name)
    if ($null -eq $entry) { Fail "no $name in the package" }
    New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
    $in = $entry.Open()
    try {
        $out = [IO.File]::Create($target)
        try { $in.CopyTo($out) } finally { $out.Dispose() }
    } finally { $in.Dispose() }
}

function Test-Text([string]$file, [string]$text) {
    [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($file)).Contains($text)
}

if (-not $GameDir) { Fail 'the game folder is not set: pass -GameDir or set SHORELANDS_DIR' }
$GameDir = $GameDir.TrimEnd('\', '/')
if (-not (Test-Path (Join-Path $GameDir 'BepInEx/core/BepInEx.dll'))) { Fail "BepInEx is not installed in '$GameDir'" }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Force $cache, $dist | Out-Null
$gameProperty = "-p:GameDir=$GameDir"

$version = (& dotnet msbuild (Join-Path $root 'src') -nologo -getProperty:Version $gameProperty | Out-String).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { Fail "not a version: '$version'" }
$changelog = Get-Content (Join-Path $root 'CHANGELOG.md')
$heading = $changelog | Where-Object { $_ -match "^## \[$([regex]::Escape($version))\] - (\d{4})-(\d{2})-(\d{2})\s*$" } | Select-Object -First 1
if (-not $heading) { Fail "CHANGELOG.md has no entry for $version" }
$null = $heading -match '(\d{4})-(\d{2})-(\d{2})'
$date = New-Object DateTimeOffset ([int]$Matches[1]), ([int]$Matches[2]), ([int]$Matches[3]), 0, 0, 0, ([TimeSpan]::Zero)

$tests = & dotnet run --project (Join-Path $root 'tests') -c Release -nologo -v q $gameProperty
if ($LASTEXITCODE -ne 0) {
    $tests | Where-Object { $_ -notmatch '^PASS ' } | Write-Host
    Fail 'the tests failed'
}
Write-Host "tests: $(@($tests | Where-Object { $_ -match '^PASS ' }).Count) passed"
Invoke-Dotnet build (Join-Path $root 'src') -c Release -nologo -v q --no-incremental $gameProperty | Out-Null
$dll = Join-Path $root 'src/bin/Release/ShorelandsBillboard.dll'

# what the package holds
$stage = Join-Path $cache 'stage'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
$target = Join-Path $stage $pluginPath
New-Item -ItemType Directory -Force (Join-Path $target 'licenses'), (Join-Path $target 'docs') | Out-Null
Copy-Item $dll, (Join-Path $root 'README.md'), (Join-Path $root 'CHANGELOG.md'), (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY-NOTICES.txt') $target
Copy-Item (Join-Path $root 'licenses/LGPL-2.1.txt') (Join-Path $target 'licenses')
Copy-Item (Join-Path $root 'docs/billboard.png') (Join-Path $target 'docs')

$native = [IO.Compression.ZipFile]::OpenRead((Get-Package $packages[0]))
try {
    foreach ($name in 'libvlc.dll', 'libvlccore.dll') { Export-Entry $native "build/x64/$name" (Join-Path $target "libvlc/$name") }
    foreach ($module in $modules) {
        $folder, $name = $module.Split('/')
        Export-Entry $native "build/x64/plugins/$folder/lib${name}_plugin.dll" (Join-Path $target "libvlc/plugins/$folder/lib${name}_plugin.dll")
    }
} finally { $native.Dispose() }
$managed = [IO.Compression.ZipFile]::OpenRead((Get-Package $packages[1]))
try { Export-Entry $managed 'lib/netstandard2.1/LibVLCSharp.dll' (Join-Path $target 'LibVLCSharp.dll') } finally { $managed.Dispose() }

# only LGPL modules of libVLC: each module carries its licence text, and the notices file names each
$notices = Get-Content -Raw (Join-Path $root 'THIRD-PARTY-NOTICES.txt')
$pluginsDir = Join-Path $target 'libvlc/plugins'
foreach ($file in Get-ChildItem -Recurse -File $pluginsDir) {
    if (-not (Test-Text $file.FullName 'GNU Lesser General Public License, version 2.1 or later')) { Fail "no LGPL notice in $($file.Name)" }
    if (Test-Text $file.FullName 'terms of the GNU General Public License') { Fail "GPL module: $($file.Name)" }
    $listed = "$($file.Directory.Name)\$($file.Name)"
    if (-not $notices.Contains($listed)) { Fail "THIRD-PARTY-NOTICES.txt does not list $listed" }
}
foreach ($package in $packages) {
    if (-not $notices.Contains("$($package.Id) $($package.Version)") -or -not $notices.Contains($package.Sha256)) {
        Fail "THIRD-PARTY-NOTICES.txt does not name $($package.Id) $($package.Version) and its checksum"
    }
}
$files = @(Get-ChildItem -Recurse -File $stage)
$leftovers = @($files | Where-Object { $_.Name -match '\.pdb$|\.deps\.json$|\.old-' })
if ($leftovers.Count -gt 0) { Fail "leftovers in the package: $($leftovers.Name -join ', ')" }

# sorted entries with the changelog's date, so the same sources give the same zip
$zip = Join-Path $dist "ShorelandsBillboard-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
$prefix = (Resolve-Path $stage).Path.Length + 1
$names = [string[]]@($files | ForEach-Object { $_.FullName.Substring($prefix).Replace('\', '/') })
[Array]::Sort($names, [StringComparer]::Ordinal)
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in $names) {
        $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $date
        $out = $entry.Open()
        try {
            $bytes = [IO.File]::ReadAllBytes((Join-Path $stage $name))
            $out.Write($bytes, 0, $bytes.Length)
        } finally { $out.Dispose() }
    }
} finally { $archive.Dispose() }
Remove-Item -Recurse -Force $stage

Write-Host "ShorelandsBillboard $version, $($names.Count) files, $([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB"
Write-Host "SHA256 $((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())  $zip"
