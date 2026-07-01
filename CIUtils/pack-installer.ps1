<#
.SYNOPSIS
    构建 TuneLab 离线单文件安装器（Windows x64）。

.DESCRIPTION
    架构（单份 Avalonia/Skia）：
      1) 框架依赖发布主程序 TuneLab      -> 暂存目录 stage\；
      2) 框架依赖发布向导 TuneLab.Setup  -> 同一 stage\（复用 app 已有的 Avalonia/Skia/GUI dll，不重复打包）；
      3) 把 stage\（app + 向导）打成 zip 载荷；
      4) 框架依赖单文件发布极小外层解压器 TuneLab.Setup.Stub -> stub exe（无 Avalonia）；
      5) 拼接：stub exe + 载荷 zip + 24 字节 footer(magic8 + offset8LE + length8LE)
         -> 最终 TuneLab-Setup-<version>-win-x64.exe。
    运行时：外层 stub 读尾部 footer、解压 stage 到临时目录、运行其中的 TuneLab.Setup.exe 向导；
    向导从所在目录整体铺到安装目录（向导自身一并落地，成为卸载器/更新器）。
    框架依赖：不打包 .NET 运行时；裸机首启弹微软官方 .NET 提示。

.PARAMETER Version
    回填到安装器/被装应用的版本号。未显式传入时从 TuneLab.csproj 的 <Version> 读取。

.PARAMETER Configuration
    Release / Debug，默认 Release。

.PARAMETER OutputPath
    最终安装器 exe 输出路径。默认 TuneLab.Setup\bin\installer\TuneLab-Setup-<version>-win-x64.exe。

.EXAMPLE
    pwsh CIUtils/pack-installer.ps1
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = 'Release',
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

# 仓库根目录（本脚本位于 CIUtils/ 下）
$repoRoot = Split-Path -Parent $PSScriptRoot
$runtime  = 'win-x64'
$magic    = 'TLSFX1'  # 之后补两个 NUL 凑满 8 字节

$appProj   = Join-Path $repoRoot 'TuneLab\TuneLab.csproj'
$setupProj = Join-Path $repoRoot 'TuneLab.Setup\TuneLab.Setup.csproj'
$stubProj  = Join-Path $repoRoot 'TuneLab.Setup.Stub\TuneLab.Setup.Stub.csproj'

# 版本号真源是 TuneLab.csproj 的 <Version>；未显式传 -Version 时从中读取。
if (-not $Version) {
    $m = [regex]::Match((Get-Content $appProj -Raw), '<Version>\s*([^<\s]+)\s*</Version>')
    if (-not $m.Success) { throw "No <Version> found in $appProj; pass -Version explicitly." }
    $Version = $m.Groups[1].Value
    Write-Host "Version (from TuneLab.csproj) = $Version" -ForegroundColor Yellow
}

# 工作目录（构建产物，随 bin 一并被 .gitignore 忽略）
$work     = Join-Path $repoRoot 'TuneLab.Setup\bin\pack'
$stageDir = Join-Path $work 'stage'       # app + 向导，共享一份 Avalonia/Skia
$stubDir  = Join-Path $work 'stub'        # 外层解压器单文件发布目录
$zipPath  = Join-Path $work 'payload.zip'

if (-not $OutputPath) {
    $installerDir = Join-Path $repoRoot 'TuneLab.Setup\bin\installer'
    $OutputPath = Join-Path $installerDir "TuneLab-Setup-$Version-$runtime.exe"
}
$installerDir = Split-Path -Parent $OutputPath

# --- 清理并重建工作目录 ---
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stageDir, $stubDir, $installerDir | Out-Null

Write-Host "==> [1/5] Publishing TuneLab app (framework-dependent $runtime)…" -ForegroundColor Cyan
dotnet publish $appProj -c $Configuration -r $runtime --self-contained false `
    -p:Version=$Version -o $stageDir
if ($LASTEXITCODE -ne 0) { throw "App publish failed." }

Write-Host "==> [2/5] Publishing wizard into the same stage (shares Avalonia/Skia)…" -ForegroundColor Cyan
dotnet publish $setupProj -c $Configuration -r $runtime --self-contained false `
    -p:Version=$Version -o $stageDir
if ($LASTEXITCODE -ne 0) { throw "Wizard publish failed." }
if (-not (Test-Path (Join-Path $stageDir 'TuneLab.Setup.exe'))) { throw "Wizard exe missing in stage." }

Write-Host "==> [3/5] Zipping payload (app + wizard)…" -ForegroundColor Cyan
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stageDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$zipLen = (Get-Item $zipPath).Length
Write-Host ("    payload.zip = {0:N1} MB" -f ($zipLen / 1MB))

Write-Host "==> [4/5] Publishing tiny extractor stub (single-file, no Avalonia)…" -ForegroundColor Cyan
dotnet publish $stubProj -c $Configuration -p:PublishSingleFile=true `
    -p:Version=$Version -o $stubDir
if ($LASTEXITCODE -ne 0) { throw "Stub publish failed." }
$stub = Join-Path $stubDir 'TuneLab.Setup.Stub.exe'
if (-not (Test-Path $stub)) { throw "Stub exe not found: $stub" }

Write-Host "==> [5/5] Assembling SFX installer…" -ForegroundColor Cyan
if (Test-Path $OutputPath) { Remove-Item $OutputPath -Force }
Copy-Item $stub $OutputPath -Force
$stubLen = (Get-Item $OutputPath).Length   # = 载荷在最终文件中的起始偏移

# 追加：载荷 zip，然后 footer
$fsOut = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Append)
try {
    $fsZip = [System.IO.File]::OpenRead($zipPath)
    try { $fsZip.CopyTo($fsOut) } finally { $fsZip.Dispose() }

    $bw = New-Object System.IO.BinaryWriter($fsOut)
    # magic: "TLSFX1" + 两个 NUL = 8 字节
    $magicBytes = [System.Text.Encoding]::ASCII.GetBytes($magic)
    $bw.Write($magicBytes)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int64]$stubLen)   # offset（BinaryWriter 写小端）
    $bw.Write([int64]$zipLen)    # length
    $bw.Flush()
    $bw.Dispose()
} finally {
    $fsOut.Dispose()
}

$finalLen = (Get-Item $OutputPath).Length
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host ("  Installer : {0}" -f $OutputPath)
Write-Host ("  Size      : {0:N1} MB  (stub {1:N1} MB + payload {2:N1} MB + 24 B footer)" -f `
    ($finalLen / 1MB), ($stubLen / 1MB), ($zipLen / 1MB))
Write-Host ("  Footer    : offset={0}  length={1}" -f $stubLen, $zipLen)
