param(
    [string]$GameDir = "",
    [switch]$Install
)

$ErrorActionPreference = "Stop"

function Resolve-ZedZoneDir([string]$explicitDir) {
    if (-not [string]::IsNullOrWhiteSpace($explicitDir)) {
        return $explicitDir.TrimEnd('\', '/')
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ZEDZONE_DIR) -and (Test-Path -LiteralPath $env:ZEDZONE_DIR)) {
        return $env:ZEDZONE_DIR.TrimEnd('\', '/')
    }

    $steamRoots = New-Object System.Collections.Generic.List[string]
    try {
        $regPath = (Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -Name "SteamPath" -ErrorAction SilentlyContinue).SteamPath
        if (-not [string]::IsNullOrWhiteSpace($regPath)) {
            $steamRoots.Add(($regPath -replace '/', '\'))
        }
    } catch {}

    foreach ($fallback in @("C:\Program Files (x86)\Steam", "C:\Program Files\Steam")) {
        if ((Test-Path -LiteralPath $fallback) -and -not $steamRoots.Contains($fallback)) {
            $steamRoots.Add($fallback)
        }
    }

    $libraries = New-Object System.Collections.Generic.List[string]
    foreach ($root in $steamRoots) {
        if (-not $libraries.Contains($root)) { $libraries.Add($root) }
        $vdf = Join-Path $root "steamapps\libraryfolders.vdf"
        if (Test-Path -LiteralPath $vdf) {
            $text = Get-Content -LiteralPath $vdf -Raw -ErrorAction SilentlyContinue
            foreach ($m in [regex]::Matches($text, '"path"\s+"([^"]+)"')) {
                $libPath = $m.Groups[1].Value -replace '\\\\', '\'
                if (-not [string]::IsNullOrWhiteSpace($libPath) -and -not $libraries.Contains($libPath)) {
                    $libraries.Add($libPath)
                }
            }
        }
    }

    foreach ($lib in $libraries) {
        $candidate = Join-Path $lib "steamapps\common\ZED ZONE"
        if (Test-Path -LiteralPath (Join-Path $candidate "BepInEx\interop\Assembly-CSharp.dll")) {
            return $candidate.TrimEnd('\', '/')
        }
    }

    throw "Could not auto-detect ZED ZONE directory. Pass -GameDir '<path>' or set ZEDZONE_DIR."
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $repoRoot "src\QuickContinue.csproj"
$cleanGameDir = Resolve-ZedZoneDir $GameDir

Write-Host "Using ZED ZONE directory: $cleanGameDir" -ForegroundColor DarkCyan

dotnet build $proj -c Release -nologo "-p:ZedZoneDir=$cleanGameDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

$dll = Get-ChildItem -Path (Join-Path $repoRoot "src\bin") -Filter "QuickContinue.dll" -Recurse |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $dll) { throw "QuickContinue.dll not found in src\bin" }

$stageDir = Join-Path $repoRoot "content\QuickContinue"
if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

Copy-Item -LiteralPath $dll.FullName -Destination (Join-Path $stageDir "QuickContinue.dll") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "mod.json") -Destination (Join-Path $stageDir "mod.json") -Force
if (Test-Path -LiteralPath (Join-Path $repoRoot "README.md")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $stageDir "README.md") -Force
}

Write-Host "Staged package -> $stageDir" -ForegroundColor Green

if ($Install) {
    if (Get-Process ZEDZONE -ErrorAction SilentlyContinue) {
        throw "ZEDZONE.exe is running; close the game before installing."
    }
    $gameModsDir = Join-Path $cleanGameDir "Mods\QuickContinue"
    New-Item -ItemType Directory -Force -Path $gameModsDir | Out-Null
    Get-ChildItem -LiteralPath $stageDir | Copy-Item -Destination $gameModsDir -Force
    Write-Host "Installed to game -> $gameModsDir" -ForegroundColor Cyan
}
