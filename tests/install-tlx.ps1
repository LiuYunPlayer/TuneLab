# Install packed test-plugin bundles into the TuneLab extensions folder
# (%AppData%\TuneLab\Extensions\<package-name>). TuneLab must be CLOSED:
# a running instance locks the extension DLLs and they cannot be replaced.
# This mirrors the host's drag-in install, minus the manual step + restart.
# Usage:  powershell -File tests/install-tlx.ps1 v1-voice [legacy-voice ...]
#         (omit names to install every .tlx under tests/tlx)
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Names)

Add-Type -AssemblyName System.IO.Compression.FileSystem
$ErrorActionPreference = 'Stop'

if (Get-Process -Name TuneLab -ErrorAction SilentlyContinue) {
    Write-Error "TuneLab is running; cannot replace extensions (DLLs locked). Close it first."
    exit 1
}

$tlxDir = Join-Path $PSScriptRoot 'tlx'
$extRoot = Join-Path $env:APPDATA 'TuneLab\Extensions'
New-Item -ItemType Directory -Force -Path $extRoot | Out-Null

if (-not $Names -or $Names.Count -eq 0) {
    $Names = Get-ChildItem $tlxDir -Filter *.tlx | ForEach-Object { $_.BaseName }
}

foreach ($n in $Names) {
    $tlx = Join-Path $tlxDir "$n.tlx"
    if (-not (Test-Path $tlx)) { Write-Warning "missing $tlx, skipped"; continue }

    # Package folder name = description.json "name" (host uses this); fall back to file name.
    $pkg = $n
    $zip = [IO.Compression.ZipFile]::OpenRead($tlx)
    try {
        $entry = $zip.GetEntry('description.json')
        if ($entry) {
            $sr = New-Object IO.StreamReader($entry.Open())
            try {
                # Malformed description.json (e.g. the bad-manifest fixture) must not abort the
                # whole install — fall back to the file-name folder and let the host reject it.
                $desc = $sr.ReadToEnd() | ConvertFrom-Json -ErrorAction Stop
                if ($desc.name) { $pkg = $desc.name }
            } catch { Write-Warning "bad description.json in $n; using file name as folder" }
            finally { $sr.Close() }
        }
    } finally { $zip.Dispose() }

    # Remove prior installs of the same plugin: both the package-name folder and the
    # file-name folder (legacy installs done before description.json carried a name),
    # otherwise two folders share one id and collide.
    foreach ($folder in (@($pkg, $n) | Select-Object -Unique)) {
        $p = Join-Path $extRoot $folder
        if (Test-Path $p) { Remove-Item $p -Recurse -Force }
    }

    [IO.Compression.ZipFile]::ExtractToDirectory($tlx, (Join-Path $extRoot $pkg))
    Write-Host "installed  $n  ->  $pkg"
}
