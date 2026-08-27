# =============================================================================
#  build.ps1 - compile HebrewBooks from source (Release), no obfuscation.
#  Driven by  בנה.bat . Safe to run on its own:  pwsh -File build.ps1
#
#  -Publish   also produce a self-contained win-x86 app folder in publish\app
#             (what a release is packed from), with the runtime assets overlaid.
# =============================================================================
param(
    [switch]$Publish,
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location $root

# ILSpy's ilspycmd and vpk are .NET 6/global tools; the machine only has newer
# runtimes, so let every dotnet child process roll forward. Harmless for builds.
$env:DOTNET_ROLL_FORWARD = "LatestMajor"

function Ensure-Assets {
    # The heavy redistributables (qpdf, pdfium, ICU data, cite.db, synonyms.db)
    # are git-ignored - too big for source control. Repopulate them from an
    # installed copy of the app so a release can still be packed on this machine.
    $assets = Join-Path $root "assets\runtime"
    $needed = @(
        "cite.db", "icudt63.dll", "synonyms.db",
        "qpdf\qpdf.exe", "x86\pdfium.dll"
    )
    $missing = $needed | Where-Object { -not (Test-Path (Join-Path $assets $_)) }
    if (-not $missing) { return }

    $installed = Join-Path $env:LOCALAPPDATA "HebrewBooks\current"
    if (-not (Test-Path $installed)) {
        Write-Host "  Missing heavy runtime assets and no installed copy to take them from:" -ForegroundColor Yellow
        $missing | ForEach-Object { Write-Host "    assets\runtime\$_" }
        Write-Host "  Install HebrewBooks once (so $installed exists) and run again," -ForegroundColor Yellow
        Write-Host "  or drop the files in by hand. The build itself does not need them;" -ForegroundColor Yellow
        Write-Host "  only packing a runnable release does." -ForegroundColor Yellow
        return
    }
    Write-Host "  Restoring heavy runtime assets from $installed ..."
    foreach ($rel in @("cite.db","icudt63.dll","synonyms.db")) {
        $src = Join-Path $installed $rel
        if (Test-Path $src) { Copy-Item $src (Join-Path $assets $rel) -Force }
    }
    foreach ($dir in @("qpdf","x86")) {
        $src = Join-Path $installed $dir
        if (Test-Path $src) { Copy-Item $src (Join-Path $assets $dir) -Recurse -Force }
    }
}

Write-Host ""
Write-Host "  Restoring + building HebrewBooks ($Configuration) ..." -ForegroundColor Cyan
dotnet build "$root\HebrewBooks.sln" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if ($Publish) {
    Ensure-Assets
    $pub = Join-Path $root "publish\app"
    if (Test-Path (Join-Path $root "publish")) { Remove-Item (Join-Path $root "publish") -Recurse -Force }
    New-Item -ItemType Directory -Path $pub -Force | Out-Null

    Write-Host "  Publishing self-contained win-x86 app ..." -ForegroundColor Cyan
    dotnet publish "$root\src\HebrewBooks.UI\HebrewBooks.UI.csproj" -c $Configuration -r win-x86 --self-contained true -o $pub --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish (UI) failed." }

    $hb = Join-Path $root "publish\hbsearch"
    dotnet publish "$root\src\hbsearch\hbsearch.csproj" -c $Configuration -r win-x86 --self-contained true -o $hb --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish (hbsearch) failed." }
    foreach ($f in @("hbsearch.exe","hbsearch.dll","hbsearch.runtimeconfig.json","hbsearch.deps.json")) {
        $s = Join-Path $hb $f
        if (Test-Path $s) { Copy-Item $s (Join-Path $pub $f) -Force }
    }

    Write-Host "  Overlaying runtime assets ..." -ForegroundColor Cyan
    Copy-Item (Join-Path $root "assets\runtime\*") $pub -Recurse -Force

    Write-Host ""
    Write-Host "  Published app:  $pub" -ForegroundColor Green
}

Write-Host ""
Write-Host "  Build OK." -ForegroundColor Green
