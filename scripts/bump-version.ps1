<#
.SYNOPSIS
    Bumps GTA Atlas's version, commits and tags it.

.DESCRIPTION
    The version lives in <Version> in Directory.Build.props (shared by every project). Pushing the vX.Y.Z tag
    triggers .github/workflows/release.yml, which checks the tag against that element, then builds, tests and
    publishes the release.

.EXAMPLE
    ./scripts/bump-version.ps1 patch          # 0.1.0 -> 0.1.1, commit + tag v0.1.1 (local only)
    ./scripts/bump-version.ps1 minor -Push    # 0.1.1 -> 0.2.0, commit + tag, push branch and tag
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('patch', 'minor', 'major')]
    [string]$Part,

    [switch]$Push
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$props = Join-Path $root 'Directory.Build.props'

function Git {
    git -C $root @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed (exit $LASTEXITCODE)" }
}

if (git -C $root status --porcelain) {
    throw 'Working tree has uncommitted changes. Commit or stash them first.'
}

$text = [IO.File]::ReadAllText($props)
$pattern = '<Version>(\d+)\.(\d+)\.(\d+)</Version>'
$found = [regex]::Match($text, $pattern)
if (-not $found.Success) {
    throw "No <Version>X.Y.Z</Version> in $props"
}

$v = 1..3 | ForEach-Object { [int]$found.Groups[$_].Value }
$current = $v -join '.'
$v = switch ($Part) {
    'major' { ($v[0] + 1), 0, 0 }
    'minor' { $v[0], ($v[1] + 1), 0 }
    'patch' { $v[0], $v[1], ($v[2] + 1) }
}
$next = $v -join '.'
$tag = "v$next"

if (git -C $root tag --list $tag) {
    throw "Tag $tag already exists."
}

# Only the version text changes; keep BOM and line endings as they are.
$bytes = [IO.File]::ReadAllBytes($props)
$bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
[IO.File]::WriteAllText($props, [regex]::Replace($text, $pattern, "<Version>$next</Version>", 1), [Text.UTF8Encoding]::new($bom))

Git add -- $props
Git commit -m "Release $tag"
Git tag -a $tag -m $tag

Write-Host "Bumped $current -> $next (tag $tag)" -ForegroundColor Green

$branch = git -C $root rev-parse --abbrev-ref HEAD
if ($branch -ne 'main') {
    Write-Warning "On '$branch', not main."
}

if ($Push) {
    Git push origin $branch
    Git push origin $tag
    Write-Host "Pushed $branch and $tag. The Release workflow builds and publishes the release." -ForegroundColor Green
}
else {
    Write-Host "Not pushed. When ready: git push origin $branch; git push origin $tag"
}
