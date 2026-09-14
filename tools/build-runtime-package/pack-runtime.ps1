# Assemble an official TuneLab "runtime" resource package (.tlx + sha256).
#
#   pwsh tools/build-runtime-package/pack-runtime.ps1 -Flavor python -Version 3.12.7 -Rid win-x64
#       -> downloads from runtime-sources.json, extracts into runtime/, zips to dist/runtime-win-x64-python-3.12.7.tlx
#
#   pwsh tools/build-runtime-package/pack-runtime.ps1 -Flavor lua -Version 5.4.6 -Rid win-x86 -FromDir C:\built\lua-5.4.6
#       -FromDir skips the network: point it at a prepared tree (a self-compiled interpreter counts —
#       design doc 8.6; the tree must expose runtime/<exe> or runtime/bin/<exe> after packing).
#
# Layout contract (RuntimeLocator.ResolvePackageExe):  win: runtime\<exe>   unix: runtime/bin/<exe>
#   -layout root   : archive already has python.exe / node.exe at its root  -> copy straight in
#   -layout prefix : archive wraps one top dir (python/, node-vX/)           -> flatten it
param(
    [Parameter(Mandatory)][ValidateSet('python','node','lua','luajit')][string]$Flavor,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Rid,
    [string]$FromDir = '',
    [string]$MapPath = (Join-Path $PSScriptRoot 'runtime-sources.json'),
    [string]$OutDir = (Join-Path $PSScriptRoot 'dist')
)
$ErrorActionPreference = 'Stop'

$pkgName  = "runtime-$Rid-$Flavor-$Version"
$stage    = Join-Path ([IO.Path]::GetTempPath()) "tunelab-runtime-$pkgName-$([Guid]::NewGuid().ToString('N'))"
$runtime  = Join-Path $stage 'runtime'
$manifest = Join-Path $stage 'manifest.json'
New-Item -ItemType Directory -Force -Path $runtime | Out-Null

$srcExe = $FromDir
if (-not $srcExe) {
    $map = Get-Content $MapPath -Raw | ConvertFrom-Json
    $entry = $map.$Flavor.versions.$Version.$Rid
    if (-not $entry -or -not $entry.url) { throw "no download for $Flavor $Version on $Rid; pass -FromDir with a prepared tree" }
    $archive = Join-Path ([IO.Path]::GetTempPath()) "tunelab-dl-$pkgName"
    Write-Host "downloading $($entry.url)"
    Invoke-WebRequest -Uri $entry.url -OutFile $archive
    $extracted = Join-Path ([IO.Path]::GetTempPath()) "tunelab-x-$pkgName-$([Guid]::NewGuid().ToString('N'))"
    if ($archive -match '\.zip$') {
        Expand-Archive -LiteralPath $archive -DestinationPath $extracted
    } elseif ($archive -match '\.tar\.(gz|xz)$') {
        New-Item -ItemType Directory -Force -Path $extracted | Out-Null
        tar -xf $archive -C $extracted
        if ($LASTEXITCODE) { throw "tar failed: $LASTEXITCODE" }
    } else { throw "unsupported archive type" }
    if ($entry.layout -eq 'prefix') {
        $top = Get-ChildItem -LiteralPath $extracted
        if (@($top).Count -ne 1 -or $top.PSIsContainer -eq $false) { throw "expected a single top-level dir in archive" }
        $srcExe = $top.FullName
    } else {
        $srcExe = $extracted
    }
}

Copy-Item -Path (Join-Path $srcExe '*') -Destination $runtime -Recurse -Force

# exe visibility per platform (RuntimeLocator tries runtime/<exe> then runtime/bin/<exe>)
$plat = $Rid.Split('-')[0]
$exeName = switch ($Flavor) {
    'python' { if ($plat -eq 'win') { 'python.exe' } else { 'python3' } }
    'node'   { if ($plat -eq 'win') { 'node.exe' }   else { 'node' } }
    'lua'    { if ($plat -eq 'win') { 'lua.exe' }    else { 'lua' } }
    'luajit' { if ($plat -eq 'win') { 'luajit.exe' } else { 'luajit' } }
}
$found = Get-ChildItem -Path $runtime -Recurse | Where-Object { $_.Name -in @($exeName, [IO.Path]::GetFileNameWithoutExtension($exeName), 'python', 'python3') } | Select-Object -First 1
if (-not $found) { Write-Warning "no '$exeName' under runtime/ — verify layout before publishing" }
if ($plat -ne 'win') {
    # keep unix exec bits (Copy-Item on Windows may drop them; tar path preserves)
    if ($found -and $found.FullName -match 'bin') { chmod +x $found.FullName 2>$null }
}

$manifestObj = [ordered]@{
    id              = "com.tunelab.runtime.$Rid.$Flavor"
    name            = "TuneLab $Flavor Runtime ($Version, $Rid)"
    version         = "1.0.0"
    author          = "TuneLab"
    description     = "Interpreter distribution for scripted extensions ($Flavor $Version, $Rid)."
    type            = 'runtime'
    flavor          = $Flavor
    'runtime-version' = $Version
    platforms       = @($Rid)
}
$manifestObj | ConvertTo-Json -Depth 4 | Set-Content -Path $manifest -Encoding utf8

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$outTlx = Join-Path $OutDir "$pkgName.tlx"
if (Test-Path $outTlx) { Remove-Item $outTlx -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath ([IO.Path]::ChangeExtension($outTlx, '.zip'))
Move-Item ([IO.Path]::ChangeExtension($outTlx, '.zip')) $outTlx
$hash = (Get-FileHash $outTlx -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$outTlx.sha256" -Value "$hash  $pkgName.tlx" -Encoding ascii

Remove-Item -Recurse -Force $stage
Write-Host "packed $outTlx"
Write-Host "sha256 $hash"   # pin this into the release notes / host download map
