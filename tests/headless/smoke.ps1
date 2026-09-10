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

# 跑一次 `tunelab mcp`，把一串 MCP 请求从 stdin 喂进去，把 stdout 的每行响应解析回来。
# MCP 的 stdio 传输就是"一条报文一行 JSON"，故这里不需要任何客户端库——CI 要验的是我们这一侧
# 说的话对不对，不是别人的实现。
function Invoke-Mcp([string[]]$Requests) {
    $inFile = Join-Path $sandbox "mcp-in.jsonl"
    $outFile = Join-Path $sandbox "mcp-out.jsonl"
    $errFile = Join-Path $sandbox "mcp-err.txt"
    [IO.File]::WriteAllText($inFile, ($Requests -join "`n") + "`n")

    $process = Start-Process -FilePath $cli -ArgumentList @("mcp") -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $outFile -RedirectStandardError $errFile -RedirectStandardInput $inFile
    $responses = @()
    foreach ($line in [IO.File]::ReadAllLines($outFile)) {
        if ($line.Trim()) { $responses += ($line | ConvertFrom-Json) }
    }
    return [pscustomobject]@{
        Code      = $process.ExitCode
        Responses = $responses
        Err       = [IO.File]::ReadAllText($errFile)
    }
}

# 把一段 JSON 变成能安全穿过 Start-Process 的实参。
#
# 【必须过这一道】ArgumentList 拼出来的是**一行命令行**，CommandLineToArgvW 会把裸双引号剥掉，
# 于是进程收到的是 {audio:C:\x} —— 那副样子和"真的传了个坏 JSON"一模一样，能让人查上十分钟
# （实测有人栽过；命令行为此专门诊断"一个双引号都没有"这一支）。故所有 object/array 参数一律经此函数。
function CliJson([string]$json) { return ($json -replace '"', '\"') }

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
    # 照跑、回报还说"跑成功了"，于是无人值守的跑批"通过"了却什么都没测到（实测栽过一次）。
    #
    # 而"把 JSON 塞进 Windows 命令行"有【两个互不相干的坑】，两条错误消息必须分得开：
    #   A 引号被外层 shell 剥掉（见 CliJson 的说明）——JSON 本身没错，是它没原样到进程里来；
    #   B 引号活着，但路径里的反斜杠没在 JSON 里双写。
    # 两支都退 2，所以【只断言退出码等于什么都没断言】：本组曾经就是这么假绿的——那时反斜杠提示是
    # 无条件附在消息末尾的，于是无论传什么坏 JSON，"匹配到 backslash"这条都恒真。现在各认各自的那句话，
    # 最后一条是【正对照】：转义 + 正斜杠必须解析通过、退到"没有这个脚本"，否则前两条的红绿都不可信。
    $badJson = '{"audio":"C:\tmp\a.wav"}'
    $r = Invoke-Cli @("--headless", "script", "run-saved", "--name", "whatever", "--inputs", $badJson)
    Check "quotes eaten by the shell is diagnosed as exactly that" ($r.Code -eq 2 -and $r.Err -match "no double quotes in it at all") $r.Err
    $r = Invoke-Cli @("--headless", "script", "run-saved", "--name", "whatever", "--inputs", (CliJson $badJson))
    Check "quotes survived but the backslash did not: the backslash hint" ($r.Code -eq 2 -and $r.Err -match "invalid escapable character" -and $r.Err -match "backslash") $r.Err
    $r = Invoke-Cli @("--headless", "script", "run-saved", "--name", "whatever", "--inputs", (CliJson '{"audio":"C:/tmp/a.wav"}'))
    Check "control: escaped quotes + forward slashes really do parse" ($r.Code -eq 1 -and $r.Err -match "no script named") $r.Err

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

    # ── 12. 动作面（issue #150）：这里【没有编辑器】，故三条都必须如实说做不到。
    #      要点是别把"这个进程永远不会有动作"说成"暂时还没有"——后者会让调用方一直重试；
    #      也别让 run 报成"没有这个 id"，那是把结构性缺席说成拼写错误。
    Write-Host "12. the action surface admits it cannot work without an editor"
    $r = Invoke-Cli @("--headless", "action", "list")
    Check "list exits 0 (an empty catalog is not an error)" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"
    Check "and says why it is empty" ($r.Out -match "No editor is present in this process") $r.Out
    $r = Invoke-Cli @("--headless", "--yes", "action", "run", "--id", "transport.play")
    Check "run exits 1" ($r.Code -eq 1) "exit $($r.Code)"
    Check "blames the missing editor, not the id" ($r.Err -match "No editor is present in this process" -and -not ($r.Err -match "no action with id")) $r.Err
    Check "and points at attaching to a running TuneLab" ($r.Err -match "attach") $r.Err
    $r = Invoke-Cli @("--headless", "editor", "status")
    Check "editor status exits 0" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"
    Check "and reports no editor instead of inventing a state" ($r.Out -match "No editor is present in this process") $r.Out

    # ── 13. issue #150 三期之后补的那几族命令，在无头下各自的实话：
    #      · project open 要的是"用户此刻开着的那份文档"，无头里根本没有那个概念 → 拒绝，
    #        且要指出去哪做（attach / --project），而不是报一个看不出所以的失败；
    #      · extension install 反过来——它**不**需要编辑器（解压 + 加载而已），故坐到路径那一层才报错，
    #        那正是"在无头里也真能装"的反面证据。
    Write-Host "13. the follow-up commands are honest about what headless can and cannot do"
    $r = Invoke-Cli @("--headless", "--yes", "project", "open", "--path", (Join-Path $sandbox "nope.tlpx"))
    Check "project open exits 1" ($r.Code -eq 1) "exit $($r.Code)"
    Check "and says there is no document to swap here" ($r.Err -match "no editor in this process") $r.Err
    Check "and points at attach / --project" (($r.Err -match "attach") -and ($r.Err -match "--project")) $r.Err
    #      · project save / save-as 与 open 同一个道理，但拒绝之后要指向的是 **export**：
    #        无头跑完想留下结果，要的本来就是"写一份到这个路径"那个语义。
    $r = Invoke-Cli @("--headless", "--yes", "project", "save")
    Check "project save exits 1" ($r.Code -eq 1) "exit $($r.Code)"
    Check "and says there is no document to save here" ($r.Err -match "no editor in this process") $r.Err
    Check "and points at export, not at attach alone" ($r.Err -match "project export") $r.Err
    $r = Invoke-Cli @("--headless", "--yes", "project", "save-as", "--path", (Join-Path $sandbox "nope.tlpx"))
    Check "project save-as exits 1" ($r.Code -eq 1) "exit $($r.Code)"
    Check "and refuses on the missing document, not on the path" (($r.Err -match "no editor in this process") -and -not ($r.Err -match "no folder at")) $r.Err
    #      · project export-audio 在无头里是**真能跑的**（渲染是纯计算，不碰音频设备），故这里不测"拒绝"，
    #        测的是它拒绝的那两件该拒绝的事：扩展名不是音频格式（要指向 project export，不能含糊成
    #        "格式不支持"）、以及空工程（混音长度自带一秒尾，不拦就会一本正经地渲出一秒静音并报成功）。
    $r = Invoke-Cli @("--headless", "--yes", "project", "export-audio", "--path", (Join-Path $sandbox "mix.tlpx"))
    Check "project export-audio rejects a project extension" ($r.Code -eq 1) "exit $($r.Code)"
    Check "and points at project export instead" ($r.Err -match "project export") $r.Err
    $r = Invoke-Cli @("--headless", "--yes", "project", "export-audio", "--path", (Join-Path $sandbox "mix.wav"))
    Check "an empty project is refused rather than rendered as silence" ($r.Code -eq 1) "exit $($r.Code)"
    Check "and says the project is empty" ($r.Err -match "this project is empty") $r.Err
    Check "and wrote nothing" (-not (Test-Path (Join-Path $sandbox "mix.wav"))) "a file was written anyway"
    $r = Invoke-Cli @("--headless", "--yes", "extension", "install", "--path", (Join-Path $sandbox "nope.tlx"))
    Check "extension install exits 1" ($r.Code -eq 1) "exit $($r.Code)"
    Check "and fails on the path, not on the missing editor" (($r.Err -match "there is no file at") -and -not ($r.Err -match "no editor")) $r.Err
    $r = Invoke-Cli @("--headless", "--yes", "extension", "uninstall", "--packageId", "com.nobody.nothing")
    Check "extension uninstall exits 1" ($r.Code -eq 1) "exit $($r.Code)"
    Check "and points at what is installed" ($r.Err -match "Installed:") $r.Err

    # ── MCP：这一段【刻意不开 TuneLab】。那正是这个 server 的存在理由——宿主没开时它仍然活着，
    #    还能列出全部能力，并在对话里说清"请先启动 TuneLab"，而不是从工具列表里静默消失。
    Write-Host "14. the MCP server serves the same command surface, with or without TuneLab running"
    $r = Invoke-Mcp @(
        '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}',
        '{"jsonrpc":"2.0","method":"notifications/initialized"}',
        '{"jsonrpc":"2.0","id":2,"method":"tools/list"}',
        '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"tunelab_help","arguments":{"command":"project export"}}}',
        '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"docs_read","arguments":{"subcommand":"manual","arguments":{}}}}',
        '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"project_read","arguments":{"subcommand":"status","arguments":{}}}}',
        '{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"setting_read","arguments":{"subcommand":"nope","arguments":{}}}}',
        '{"jsonrpc":"2.0","id":7,"method":"resources/list"}'
    )
    Check "the server exits cleanly when the client goes away" ($r.Code -eq 0) "exit $($r.Code); $($r.Err)"
    Check "one response per request (notifications get none)" ($r.Responses.Count -eq 7) "$($r.Responses.Count) response(s)"

    $init = $r.Responses | Where-Object { $_.id -eq 1 }
    Check "initialize answers in the version the client asked for" ($init.result.protocolVersion -eq "2024-11-05") $init.result.protocolVersion
    Check "and tells the client how this surface is organized" ($init.result.instructions -match "subcommand") $init.result.instructions

    $tools = ($r.Responses | Where-Object { $_.id -eq 2 }).result.tools
    Check "listing the tools does not need TuneLab" ($tools.Count -ge 15) "$($tools.Count) tool(s)"
    $names = $tools | ForEach-Object { $_.name }
    Check "grouped per subject and kind" (($names -contains "project_read") -and ($names -contains "project_edit") -and ($names -contains "tunelab_help")) ($names -join ", ")
    $edit = $tools | Where-Object { $_.name -eq "project_edit" }
    $read = $tools | Where-Object { $_.name -eq "project_read" }
    Check "annotations say which ones change things" ($edit.annotations.destructiveHint -eq $true -and $read.annotations.readOnlyHint -eq $true) "edit=$($edit.annotations | ConvertTo-Json -Compress) read=$($read.annotations | ConvertTo-Json -Compress)"
    Check "the listing carries a summary, not the whole manual" ($read.description -match "PPQ, tempo" -and -not ($read.description -match "PPQ and the tempo map")) $read.description

    $help = ($r.Responses | Where-Object { $_.id -eq 3 }).result
    Check "the help gives the full manual" ($help.isError -eq $false -and $help.content[0].text -match "NOT 'save'") $help.content[0].text
    Check "and says which tool and subcommand to call" ($help.content[0].text -match 'project_edit with subcommand "export"') $help.content[0].text

    $manual = ($r.Responses | Where-Object { $_.id -eq 4 }).result
    Check "the docs answer without TuneLab running" ($manual.isError -eq $false -and $manual.content[0].text -match "Chapters") $manual.content[0].text

    $status = ($r.Responses | Where-Object { $_.id -eq 5 }).result
    Check "a command that needs TuneLab says so instead of failing silently" ($status.isError -eq $true) ($status | ConvertTo-Json -Compress)
    Check "and points at the bridge switch" ($status.content[0].text -match "Command Bridge") $status.content[0].text

    $unknown = ($r.Responses | Where-Object { $_.id -eq 6 }).result
    Check "an unknown subcommand lists the ones there are" ($unknown.isError -eq $true -and $unknown.content[0].text -match "It takes: list") $unknown.content[0].text

    $protocol = $r.Responses | Where-Object { $_.id -eq 7 }
    Check "an unimplemented method is a protocol error, not a tool result" ($protocol.error.code -eq -32601) ($protocol | ConvertTo-Json -Compress)
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
