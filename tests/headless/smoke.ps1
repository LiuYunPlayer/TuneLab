# 无头宿主的冒烟：无人值守跑一串命令并断言（docs/command-surface.md §8）。
#
#   pwsh tests/headless/smoke.ps1 [-Configuration Debug]
#
# 它验证的是【一整条链路】而不是某一条命令：进程起来 → 插件加载 → 工程在内存 → 命令连着跑
# （前一条的编辑后一条看得见）→ 导出落盘 → 退出码如实。单元测试碰不到这些，因为它们全都
# 只在真的起一个宿主进程之后才成立。
#
# 数据目录用 TUNELAB_DATA_DIR 隔到临时沙盒：既不读开发机/CI 机上真实的设置与已装插件（那会让
# 断言随机器而变），也不写坏它们。
[CmdletBinding()]
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$cli = Join-Path $repoRoot "TuneLab.Cli/bin/$Configuration/net8.0/TuneLab.Cli.exe"
if (-not (Test-Path $cli)) {
    $cli = Join-Path $repoRoot "TuneLab.Cli/bin/$Configuration/net8.0/TuneLab.Cli"
}
if (-not (Test-Path $cli)) {
    Write-Host "smoke: no TuneLab.Cli in bin/$Configuration. Build it first: dotnet build TuneLab.sln -c $Configuration"
    exit 2
}

$sandbox = Join-Path ([IO.Path]::GetTempPath()) ("TuneLab.HeadlessSmoke." + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force -Path $sandbox | Out-Null
$env:TUNELAB_DATA_DIR = Join-Path $sandbox "data"

Write-Host "cli:     $cli"
Write-Host "sandbox: $sandbox"
Write-Host ""

$script:Failures = 0

function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) {
        Write-Host "  PASS  $name"
    } else {
        $script:Failures++
        Write-Host "  FAIL  $name"
        if ($detail) { Write-Host "        $detail" }
    }
}

# 跑一次 CLI，把 stdout / stderr / 退出码都拿回来。
# stdin 一律重定向到空文件：那正是 CI 里的样子（问不着用户），也让本脚本在开发机的交互终端里
# 跑出同样的结果，不会停在一个确认提示上等回车。
function Invoke-Cli([string[]]$CliArgs) {
    $outFile = Join-Path $sandbox "stdout.txt"
    $errFile = Join-Path $sandbox "stderr.txt"
    $nulFile = Join-Path $sandbox "empty-stdin.txt"
    if (-not (Test-Path $nulFile)) { [IO.File]::WriteAllText($nulFile, "") }

    $process = Start-Process -FilePath $cli -ArgumentList $CliArgs -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $outFile -RedirectStandardError $errFile -RedirectStandardInput $nulFile
    return [pscustomobject]@{
        Code = $process.ExitCode
        Out  = [IO.File]::ReadAllText($outFile)
        Err  = [IO.File]::ReadAllText($errFile)
    }
}

$exported = Join-Path $sandbox "smoke.tlpx"
$commandList = Join-Path $sandbox "commands.txt"
[IO.File]::WriteAllText($commandList, @"
# 读 → 改设置 → 用脚本改工程 → 再读（看得见上一条的改动）→ 导出
project status
setting set --key AutoSaveInterval --value 20
script run --code "const t = tl.currentProject().addTrack(); const p = t.addPart({pos: 0, endOffset: 1920}); p.addNote({pos: 0, dur: 480, pitch: 60, lyric: ""la""}); print('notes: ' + p.notes().length);"
project status
project export --path "$exported"
"@, (New-Object Text.UTF8Encoding $false))

try {
    # ── 1. 帮助离线可用：不连宿主、不起 headless，也该列得出命令树。
    Write-Host "1. offline help"
    $r = Invoke-Cli @("--help")
    Check "exit 0" ($r.Code -eq 0) "exit $($r.Code)"
    Check "lists the command tree" ($r.Out -match "project status") $r.Out

    # ── 2. 无人值守 + 没给授权：读照跑，写一律不落地，且【说清是问不着而不是被拒绝】。
    Write-Host "2. unattended, no authorization"
    $r = Invoke-Cli @("--headless", "--commands", $commandList)
    Check "exit 0 (every command did report an outcome)" ($r.Code -eq 0) "exit $($r.Code)"
    Check "reads still work" ($r.Out -match "PPQ=480") $r.Out
    Check "says it could not ask, not that anyone refused" ($r.Out -match "no UI is available to ask") $r.Out
    Check "warns on stderr about non-interactive stdin" ($r.Err -match "stdin is not interactive") $r.Err
    Check "nothing was exported" (-not (Test-Path $exported)) "$exported exists"

    # ── 3. 授权给了：整串跑通，且【后一条看得见前一条的改动】——一个进程一个工程，这是批量的意义。
    Write-Host "3. authorized run"
    $r = Invoke-Cli @("--headless", "--commands", $commandList, "--yes")
    Check "exit 0" ($r.Code -eq 0) "$($r.Err)"
    Check "the setting was changed" ($r.Out -match 'Changed "AutoSaveInterval"') $r.Out
    Check "the script applied its edits" ($r.Out -match "Applied 3 edit\(s\)") $r.Out
    Check "the later status sees them" ($r.Out -match "parts=1, notes=1") $r.Out
    Check "the export landed" (Test-Path $exported) "no file at $exported"
    Check "every command ran" ($r.Err -match "5 command\(s\) ok") $r.Err

    # ── 4. 装载工程：刚导出的那个文件再打开，内容对得上。
    Write-Host "4. open an existing project"
    $r = Invoke-Cli @("--headless", "--project", $exported, "project", "status")
    Check "exit 0" ($r.Code -eq 0) "$($r.Err)"
    Check "the track came back" ($r.Out -match "parts=1, notes=1") $r.Out

    # ── 5. --json 打的是【结构化事实】，且 stdout 干净到可以直接喂给解析器（日志不许混进来）。
    Write-Host "5. --json"
    $r = Invoke-Cli @("--headless", "--project", $exported, "project", "status", "--json")
    Check "exit 0" ($r.Code -eq 0) "$($r.Err)"
    $parsed = $null
    try { $parsed = $r.Out | ConvertFrom-Json } catch { }
    Check "stdout parses as JSON" ($null -ne $parsed) $r.Out
    Check "and carries the facts" ($null -ne $parsed -and $parsed.ppq -eq 480) $r.Out

    # ── 6. 命令真失败要非零退出：CI 靠这一条分支。
    Write-Host "6. a failing command"
    $r = Invoke-Cli @("--headless", "setting", "set", "--key", "NoSuchKeyAtAll", "--value", "1", "--yes")
    Check "exit 1" ($r.Code -eq 1) "exit $($r.Code)"
    Check "says which key" ($r.Err -match "NoSuchKeyAtAll") $r.Err

    # ── 7. 用法错与命令失败分得开（退出码 2）。
    Write-Host "7. wrong usage"
    $r = Invoke-Cli @("--headless", "setting", "set", "--nosuchparameter", "1")
    Check "exit 2" ($r.Code -eq 2) "exit $($r.Code)"

    # object 参数收的是 JSON：解析不了必须当场报用法错。**不许降级成"当没给"**——那会让脚本拿默认值
    # 照跑、回报还说"跑成功了"，于是无人值守的跑批"通过"了却什么都没测到（Windows 路径里的反斜杠被
    # shell 吃掉一层就是这个形态，实测栽过一次）。
    $r = Invoke-Cli @("--headless", "script", "run-saved", "--name", "whatever", "--inputs", '{"audio":"C:\tmp\a.wav"}')
    Check "malformed JSON in an object parameter is a usage error" ($r.Code -eq 2) "exit $($r.Code)"
    Check "and it points at the backslash trap" ($r.Err -match "backslash") $r.Err

    # ── 8. 打不开的工程：一条命令都还没跑就该停，且说清是哪个文件。
    Write-Host "8. a project that will not open"
    $r = Invoke-Cli @("--headless", "--project", (Join-Path $sandbox "nope.tlpx"), "project", "status")
    Check "exit 1" ($r.Code -eq 1) "exit $($r.Code)"
    Check "names the file" ($r.Err -match "nope.tlpx") $r.Err

    # ── 9. 零前提：docs 组既不连桥也不起无头宿主（沙盒里根本没有运行中的 TuneLab，也没给 --headless）。
    #      这一条是"外部 agent 第一次接触"的入口：读不到说明书，它连自己能干什么都不知道。
    Write-Host "9. docs answer with no host at all"
    $r = Invoke-Cli @("docs", "script-api")
    Check "exit 0" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"
    Check "the API reference came out" ($r.Out -match "tl.currentProject") $r.Out
    Check "it never went looking for a host" (-not ($r.Err -match "command bridge")) $r.Err
    $r = Invoke-Cli @("docs", "manual")
    Check "the manual answers too" ($r.Code -eq 0 -and $r.Out.Length -gt 0) "exit $($r.Code) $($r.Err)"

    # ── 10. --search：全命令帮助语料的正则检索，同样离线。
    Write-Host "10. --search"
    $r = Invoke-Cli @("--search", "phoneme")
    Check "exit 0" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"
    Check "finds the command that mentions it" ($r.Out -match "source list") $r.Out
    $r = Invoke-Cli @("--search", "zzzznosuchword")
    Check "no match is not an error" ($r.Code -eq 0) "exit $($r.Code)"
    Check "and says so" ($r.Out -match "No command's help matches") $r.Out

    # ── 11. app info：报的是这个装置本身（排障要交的第一样东西）。
    Write-Host "11. app info"
    $r = Invoke-Cli @("--headless", "app", "info")
    Check "exit 0" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"
    Check "names a version" ($r.Out -match "TuneLab \d+\.\d+") $r.Out
    Check "points at the data dir and the log" ($r.Out -match "data directory" -and $r.Out -match "log file") $r.Out
    Check "and says there is no editor here" ($r.Out -match "editor: no") $r.Out
}
finally {
    Remove-Item -Recurse -Force $sandbox -ErrorAction SilentlyContinue
}

Write-Host ""
if ($script:Failures -gt 0) {
    Write-Host "smoke: $($script:Failures) check(s) failed."
    exit 1
}
Write-Host "smoke: all checks passed."
exit 0
