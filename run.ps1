#!/usr/bin/env pwsh
# 可靠地构建并启动 TuneLab（Debug）。
#
# 为什么需要它：中断/被锁的构建、或「仅 C# 编译」(dotnet build -t:Compile) 会在 obj/ 留下一个
# 未嵌入 Avalonia XAML 的 TuneLab.dll；之后的增量构建见它比源码新，就判定 XAML 已最新而【跳过】
# 预编译步，拷进 bin/ 后启动即报 "No precompiled XAML found for TuneLab.App"。
#
# 默认走 -t:Rebuild（强制重跑 XAML 嵌入，杜绝上述陷阱）。若确定 obj 干净、想更快，加 -Fast 走增量。
#
#   ./run.ps1          # 可靠：Rebuild + 启动
#   ./run.ps1 -Fast    # 快：增量构建 + 启动（偶尔可能撞上缺 XAML，那就去掉 -Fast 再来一次）

param([switch]$Fast)

$ErrorActionPreference = 'Stop'
$proj = Join-Path $PSScriptRoot 'TuneLab/TuneLab.csproj'
$exe  = Join-Path $PSScriptRoot 'TuneLab/bin/Debug/net8.0/TuneLab.exe'

# 关掉在跑的实例（否则锁 dll、构建拷贝失败；单实例锁也会挡新进程）。
Get-Process TuneLab -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

if ($Fast) {
    dotnet build $proj -c Debug
} else {
    dotnet build $proj -c Debug -t:Rebuild
}
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Build failed.' -ForegroundColor Red
    exit 1
}

Start-Process $exe
Write-Host 'TuneLab launched.' -ForegroundColor Green
