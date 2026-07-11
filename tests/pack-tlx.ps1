# 把测试插件包打成 .tlx（= zip，根目录含 manifest.json + dll），供 App 内拖拽/Install Extension 安装。
# .tlx 是 TuneLab 的安装单位：拖文件夹进窗口【不会】安装，只有 .tlx 文件会（见 Editor.OnDrop / InstallExtensions）。
#
# 用法：先构建测试插件，再打包：
#   dotnet build tests/TestPlugins.slnx -c Debug
#   powershell -File tests/pack-tlx.ps1      （或 pwsh tests/pack-tlx.ps1）
# 产物在 tests/tlx/*.tlx，逐个拖进 TuneLab 窗口（或扩展侧边栏 Install Extension）安装并即时加载。

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = $PSScriptRoot
$out = Join-Path $root 'tlx'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$sources = @()
$sources += Get-ChildItem -Directory (Join-Path $root 'packages') -ErrorAction SilentlyContinue
$sources += Get-ChildItem -Directory (Join-Path $root 'manifest-variants') -ErrorAction SilentlyContinue

if ($sources.Count -eq 0) {
    Write-Warning "未找到任何包。请先运行: dotnet build tests/TestPlugins.slnx -c Debug"
    return
}

foreach ($dir in $sources) {
    $tlx = Join-Path $out ($dir.Name + '.tlx')
    if (Test-Path $tlx) { Remove-Item $tlx -Force }
    # 打包文件夹【内容】到 .tlx（manifest.json/dll 落 zip 根）
    [System.IO.Compression.ZipFile]::CreateFromDirectory($dir.FullName, $tlx)
    Write-Host "packed $($dir.Name).tlx"
}

Write-Host "完成：$out"
