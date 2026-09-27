# build.ps1 - Build SpeechChem and deploy it into the game. Deploy itself is the Debug
# post-build target of the two projects (host dll + 0Harmony + Mono.Cecil + prism.dll (x86) +
# SpaceChem.exe.config + steam_appid.txt from the host; SpeechChem.Module.dll +
# SpeechChem\namemap.tsv + SpeechChem\locale from the module); this script locates the game, checks
# the deob exe prerequisite, and runs the build. Close the game first, or the HOST dll copy is
# skipped (file locked) and you'll run a stale host - the module still deploys (it's byte-loaded).
#
# Adapted from the Non-Visual Calculus installer by Rashad Naqeeb (MIT),
# https://github.com/rashadnaqeeb/NonVisualCalculus - by way of the Harkest Dungeon
# installer, https://github.com/amerikrainian/harkest-dungeon.

param(
    [switch]$Help
)

if ($Help) {
    Write-Host "Usage: .\build.ps1 [-Help]"
    Write-Host "  Builds the solution (Debug) and deploys the mod into the game folder."
    Write-Host "  Run tools\prepare-game.ps1 once first (the module compiles against game\SpaceChem-deob.exe)."
    Write-Host "  Set SPACECHEM_DIR to point at a non-default game folder."
    exit 0
}

$ErrorActionPreference = "Stop"

$GameExe = "SpaceChem.exe"
$GameFolder = "SpaceChem"

# --- Locate the game install: SPACECHEM_DIR, then every Steam library ---
$Game = $env:SPACECHEM_DIR
if (-not $Game) {
    $RegSteam = (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam" -Name InstallPath -ErrorAction SilentlyContinue).InstallPath
    $DefaultSteam = if ($RegSteam) { $RegSteam } else { "C:\Program Files (x86)\Steam" }
    $SteamPaths = @()
    if (Test-Path "$DefaultSteam\steamapps") { $SteamPaths += $DefaultSteam }
    $LibFolders = "$DefaultSteam\steamapps\libraryfolders.vdf"
    if (Test-Path $LibFolders) {
        $content = Get-Content $LibFolders -Raw
        [regex]::Matches($content, '"path"\s+"([^"]+)"') | ForEach-Object {
            $p = $_.Groups[1].Value -replace '\\\\', '\'
            if ($p -ne $DefaultSteam -and (Test-Path "$p\steamapps")) { $SteamPaths += $p }
        }
    }
    foreach ($steam in $SteamPaths) {
        $candidate = "$steam\steamapps\common\$GameFolder"
        if (Test-Path "$candidate\$GameExe") { $Game = $candidate; break }
    }
    if (-not $Game) { $Game = "C:\Program Files (x86)\Steam\steamapps\common\$GameFolder" }
}
if (-not (Test-Path "$Game\$GameExe")) {
    Write-Host "ERROR: SpaceChem not found at: $Game" -ForegroundColor Red
    Write-Host "Set the SPACECHEM_DIR environment variable to the game folder." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path "$PSScriptRoot\game\SpaceChem-deob.exe")) {
    Write-Host "ERROR: game\SpaceChem-deob.exe is missing - the module compiles against it." -ForegroundColor Red
    Write-Host "Run tools\prepare-game.ps1 once (de4dot is vendored)." -ForegroundColor Red
    exit 1
}

# --- Build (the Debug post-build targets deploy) ---
Write-Host "Building SpeechChem (game: $Game)..." -ForegroundColor Cyan
dotnet build "$PSScriptRoot\SpeechChem.sln" -c Debug -p:GameDir="$Game"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Done. Launch SpaceChem through Steam and listen for `"SpeechChem ready`"." -ForegroundColor Cyan
