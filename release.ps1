# =============================================================================
#  release.ps1 - build a HebrewBooks release with Velopack and publish it to
#  GitHub Releases on  yossi-computers/HebrewBooks-2026 . Driven by  שחרר.bat .
#
#  Pipeline:  build.ps1 -Publish  ->  vpk pack (channel 'stable')  ->
#             vpk upload github --publish
#
#  The app's own auto-updater (AppUpdateService) reads exactly this repo and the
#  'stable' channel, so a normal release rolls out to installed copies. -Draft
#  and -Prerelease stay off that path: a draft is invisible, a pre-release is on
#  the page for a manual install but no installed copy updates itself to it.
# =============================================================================
param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$Notes = "",
    [switch]$Draft,
    [switch]$Prerelease
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location $root
$env:DOTNET_ROLL_FORWARD = "LatestMajor"

$RepoUrl  = "https://github.com/yossi-computers/HebrewBooks-2026"
$Channel  = "stable"
$Id       = "HebrewBooks"
$vpk      = Join-Path $env:USERPROFILE ".dotnet\tools\vpk.exe"

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be X.Y.Z (got '$Version')." }
if ([string]::IsNullOrWhiteSpace($Notes)) { $Notes = "HebrewBooks $Version" }
if (-not (Test-Path $vpk)) { throw "vpk not found at $vpk. Install once: dotnet tool install -g vpk" }

# GitHub credentials come from the gh CLI the maintainer is already logged in to.
$token = (gh auth token 2>$null)
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "Not logged in to GitHub. Run:  gh auth login"
}

# --- Stamp the version into the tree so assembly/file versions match the release.
$props = Join-Path $root "Directory.Build.props"
$xml = Get-Content $props -Raw
$xml = $xml -replace '<Version>[^<]*</Version>',               "<Version>$Version</Version>"
$xml = $xml -replace '<AssemblyVersion>[^<]*</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>"
$xml = $xml -replace '<FileVersion>[^<]*</FileVersion>',         "<FileVersion>$Version.0</FileVersion>"
Set-Content $props $xml -Encoding UTF8

# --- Build + publish the self-contained app folder.
& "$root\build.ps1" -Publish
if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed." }
$pub = Join-Path $root "publish\app"
if (-not (Test-Path (Join-Path $pub "HebrewBooks.exe"))) { throw "publish\app\HebrewBooks.exe missing." }

# --- Pack the Velopack release (full + delta + setup + portable).
$rel = Join-Path $root "releases"
if (-not (Test-Path $rel)) { New-Item -ItemType Directory -Path $rel | Out-Null }
$icon = Join-Path $root "src\HebrewBooks.UI\resources\hebrewbooks.ico"

Write-Host ""
Write-Host "  Packing Velopack release $Version (channel $Channel) ..." -ForegroundColor Cyan
& $vpk pack --packId $Id --packVersion $Version --packDir $pub --mainExe "HebrewBooks.exe" `
    --packTitle "HebrewBooks" --channel $Channel --outputDir $rel --icon $icon
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed." }

# --- Upload to GitHub Releases. --merge lets a re-run finish a partial upload.
$args = @("upload","github","--outputDir",$rel,"--channel",$Channel,
          "--repoUrl",$RepoUrl,"--token",$token,"--merge",
          "--releaseName","HebrewBooks $Version","--tag","v$Version")
if ($Prerelease) { $args += "--pre" }
if (-not $Draft) { $args += "--publish" }   # omit --publish => leaves a draft

Write-Host ""
if ($Draft)          { Write-Host "  Uploading as a DRAFT (nobody sees it) ..." -ForegroundColor Cyan }
elseif ($Prerelease) { Write-Host "  Uploading as a PRE-RELEASE ..." -ForegroundColor Cyan }
else                 { Write-Host "  Uploading as a full public release ..." -ForegroundColor Cyan }

& $vpk @args
if ($LASTEXITCODE -ne 0) { throw "vpk upload github failed." }

Write-Host ""
Write-Host "  Released HebrewBooks $Version -> $RepoUrl/releases/tag/v$Version" -ForegroundColor Green
Write-Host "  Notes: $Notes"
