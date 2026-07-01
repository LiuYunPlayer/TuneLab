<#
.SYNOPSIS
    本地假更新服务器：一键测试整包自动更新链路（无需真实服务端）。

.DESCRIPTION
    1) 用 pack-installer.ps1 打包指定版本的安装器（默认 9.9.9，保证高于任何已装版本）；
    2) 起一个本地 HTTP 服务（TcpListener，无需管理员/URL ACL），提供：
         GET /api/app/get-update  -> {version,url,...} JSON（url 指向下方安装器）
         GET /<安装器文件名>       -> 安装器 exe 字节
    然后在另一个终端把已安装的旧版 TuneLab 指向本服务启动即可：
         $env:TUNELAB_API_BASE='http://localhost:<port>'; & "$env:LOCALAPPDATA\Programs\TuneLab\TuneLab.exe"
    App 启动即检查到新版 -> 点 Update -> 下载(带进度) -> 退出 -> 安装器覆盖 -> 重启新版。

.PARAMETER Version
    冒充的新版本号，需 > 当前已安装版本。默认 9.9.9。

.PARAMETER Port
    本地服务端口，默认 8000。

.EXAMPLE
    pwsh CIUtils/serve-mock-update.ps1
#>
[CmdletBinding()]
param(
    [string]$Version = '9.9.9',
    [int]$Port = 8000,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "==> Packing installer $Version…" -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'pack-installer.ps1') -Version $Version -Configuration $Configuration | Out-Host

$installer = Join-Path $repoRoot "TuneLab.Setup\bin\installer\TuneLab-Setup-$Version-win-x64.exe"
if (-not (Test-Path $installer)) { throw "Installer not found: $installer" }
$installerName  = Split-Path $installer -Leaf
$installerBytes = [System.IO.File]::ReadAllBytes($installer)

$json = @{
    version     = $Version
    url         = "http://localhost:$Port/$installerName"
    description = "# TuneLab $Version`n本地测试更新包。"
    publishedAt = '2026-07-01T00:00:00'
} | ConvertTo-Json -Compress
$jsonBytes = [System.Text.Encoding]::UTF8.GetBytes($json)

Write-Host ""
Write-Host "Serving mock update at http://localhost:$Port" -ForegroundColor Green
Write-Host ("  version   = {0}" -f $Version)
Write-Host ("  installer = {0} ({1:N1} MB)" -f $installerName, ($installerBytes.Length / 1MB))
Write-Host ""
Write-Host "在另一个终端启动已安装的旧版 TuneLab（版本需 < $Version）：" -ForegroundColor Yellow
Write-Host "  `$env:TUNELAB_API_BASE='http://localhost:$Port'; & `"`$env:LOCALAPPDATA\Programs\TuneLab\TuneLab.exe`""
Write-Host ""
Write-Host "Ctrl+C 停止服务。" -ForegroundColor DarkGray

function Write-Response {
    param($Stream, [string]$Status, [string]$ContentType, [byte[]]$Body)
    $len = if ($Body) { $Body.Length } else { 0 }
    $header = "HTTP/1.1 $Status`r`nContent-Type: $ContentType`r`nContent-Length: $len`r`nConnection: close`r`n`r`n"
    $hb = [System.Text.Encoding]::ASCII.GetBytes($header)
    $Stream.Write($hb, 0, $hb.Length)
    if ($len -gt 0) { $Stream.Write($Body, 0, $Body.Length) }
    $Stream.Flush()
}

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$listener.Start()
try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $buf = New-Object byte[] 8192
            $n = $stream.Read($buf, 0, $buf.Length)
            if ($n -le 0) { continue }
            $req = [System.Text.Encoding]::ASCII.GetString($buf, 0, $n)
            $firstLine = ($req -split "`r`n")[0]
            $path = ($firstLine -split ' ')[1]
            Write-Host ("  REQ {0}" -f $firstLine) -ForegroundColor DarkGray

            if ($path -like '/api/app/get-update*') {
                Write-Response $stream '200 OK' 'application/json' $jsonBytes
            }
            elseif ($path -like "*/$installerName") {
                Write-Response $stream '200 OK' 'application/octet-stream' $installerBytes
            }
            else {
                Write-Response $stream '404 Not Found' 'text/plain' ([byte[]]@())
            }
        }
        catch { Write-Host ("  ERR {0}" -f $_.Exception.Message) -ForegroundColor Red }
        finally { $client.Close() }
    }
}
finally { $listener.Stop() }
