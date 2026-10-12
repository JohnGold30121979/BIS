# BIS ERP product version auto-increment.
#
# Reads version.json, increments Patch and Build, writes the file back and prints
# BIS_* lines that Directory.Build.props parses into MSBuild properties.
#
# NOTE: this file is intentionally pure ASCII. Windows PowerShell 5.1 reads a .ps1
# file without a UTF-8 BOM using the ANSI code page, which corrupts non-ASCII text
# and breaks parsing. Russian explanations live in Directory.Build.props (XML) and
# in Docs/Versioning, where encoding is not a problem.

param(
    [Parameter(Mandatory = $true)]
    [string] $VersionFile
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($VersionFile)) {
    throw 'Missing -VersionFile: path to version.json is required.'
}

# First build on a clean machine: there is no version source yet, so seed it.
if (-not (Test-Path -LiteralPath $VersionFile)) {
    $seed = [ordered]@{
        Major         = 1
        Minor         = 0
        Patch         = 0
        Build         = 0
        Semantic      = '1.0.0'
        Informational = '1.0.0'
        LastBuildUtc  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    }
    $seedText = ($seed | ConvertTo-Json) + [Environment]::NewLine
    [System.IO.File]::WriteAllText($VersionFile, $seedText, (New-Object System.Text.UTF8Encoding($false)))
}

$raw = [System.IO.File]::ReadAllText($VersionFile)
if ([string]::IsNullOrWhiteSpace($raw)) {
    throw "Version file is empty: $VersionFile"
}

try {
    $json = $raw | ConvertFrom-Json
}
catch {
    throw "Cannot parse version JSON in $VersionFile : $($_.Exception.Message)"
}

$major = if ($null -ne $json.Major) { [int] $json.Major } else { 1 }
$minor = if ($null -ne $json.Minor) { [int] $json.Minor } else { 0 }
$patch = if ($null -ne $json.Patch) { [int] $json.Patch } else { 0 }
$build = if ($null -ne $json.Build) { [int] $json.Build } else { 0 }

# The increment runs before compilation: a failed build still advances the number.
$patch = $patch + 1
$build = $build + 1

$semantic = '{0}.{1}.{2}' -f $major, $minor, $patch
$stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$informational = '{0}+{1}#{2}' -f $semantic, $stamp, $build

$result = [ordered]@{
    Major         = $major
    Minor         = $minor
    Patch         = $patch
    Build         = $build
    Semantic      = $semantic
    Informational = $informational
    LastBuildUtc  = $stamp
}

$text = ($result | ConvertTo-Json) + [Environment]::NewLine
[System.IO.File]::WriteAllText($VersionFile, $text, (New-Object System.Text.UTF8Encoding($false)))

# These lines are captured by Directory.Build.props through ConsoleToMSBuild.
Write-Output "BIS_VERSION=$semantic"
Write-Output "BIS_BUILD=$build"
Write-Output "BIS_INFORMATIONAL=$informational"
Write-Output "BIS_BUILD_UTC=$stamp"
