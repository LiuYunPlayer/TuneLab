# PathPicker（路径选择 config）的无头自动化（配方见 docs/command-surface.md §8，模板同 tests/headless/smoke.ps1）。
#
#   pwsh tests/headless/path-picker.ps1 [-Configuration Debug]
#
# 它测的是 PathPickerConfig 能被【自动化验证的那一半】：声明如何渲染成给模型看的类型措辞
# （ConfigText.Describe）、值如何一路到达脚本 main、扩展设置那条读写路。控件本身的交互
# （点浏览按钮、系统对话框、属性面板绑定）没有自动化路径，留给 tests/PATH-PICKER-CONFIG-TEST-CASES.md 人工跑。
#
# 数据目录经 TUNELAB_DATA_DIR 隔到临时沙盒：脚本夹具与扩展夹具都装进沙盒，既不读也不写开发机上
# 真实的 %APPDATA%\TuneLab。
[CmdletBinding()]
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$cli = Join-Path $repoRoot "TuneLab.Cli/bin/$Configuration/net8.0/TuneLab.Cli.exe"
if (-not (Test-Path $cli)) {
    $cli = Join-Path $repoRoot "TuneLab.Cli/bin/$Configuration/net8.0/TuneLab.Cli"
}
if (-not (Test-Path $cli)) {
    Write-Host "path-picker: no TuneLab.Cli in bin/$Configuration. Build it first: dotnet build TuneLab.sln -c $Configuration"
    exit 2
}

$sandbox = Join-Path ([IO.Path]::GetTempPath()) ("TuneLab.PathPicker." + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force -Path $sandbox | Out-Null
$dataDir = Join-Path $sandbox "data"
$env:TUNELAB_DATA_DIR = $dataDir

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

# 同 smoke：stdin 一律接空文件，让开发机上的交互终端跑出与 CI 一样的结果。
function Invoke-Cli([string[]]$CliArgs) {
    $outFile = Join-Path $sandbox "stdout.txt"
    $errFile = Join-Path $sandbox "stderr.txt"
    $nulFile = Join-Path $sandbox "empty-stdin.txt"
    if (-not (Test-Path $nulFile)) { [IO.File]::WriteAllText($nulFile, "") }

    $process = Start-Process -FilePath $cli -ArgumentList $CliArgs -NoNewWindow -Wait -PassThru -RedirectStandardOutput $outFile -RedirectStandardError $errFile -RedirectStandardInput $nulFile
    return [pscustomobject]@{
        Code = $process.ExitCode
        Out  = [IO.File]::ReadAllText($outFile)
        Err  = [IO.File]::ReadAllText($errFile)
    }
}

# Start-Process 的 ArgumentList 是【一行命令行】，里面的引号要按 Win32 的规矩转义（\"），否则
# CommandLineToArgvW 会把它们吃掉，JSON 到达命令时已经不是 JSON 了。踩下去的样子与"真的传了个坏 JSON"
# 一模一样（都是退出码 2），最容易把【夹具自己写错了】误认成【命令面报对了错】——故统一走这里转义。
function CliJson([string]$json) { return ($json -replace '"', '\"') }

# 脚本夹具直接落进沙盒的脚本库（比 script save --code 省掉一层转义，也不必过写授权闸门）。
# 三个字段各测一件事：带过滤器 + 标题的选文件、选文件夹、单个字符串形态的 appendFileType。
$scriptsDir = Join-Path $dataDir "Scripts"
New-Item -ItemType Directory -Force -Path $scriptsDir | Out-Null
[IO.File]::WriteAllText((Join-Path $scriptsDir "PathPickerProbe.js"), @'
function getScriptInfo() { return { name: 'PathPicker Probe', context: 'global' }; }

function getInputConfig(ctx) {
  return {
    audio: PathPickerConfig.createFile('C:/default.wav')
             .appendFileType('Audio', ['*.wav', '*.mp3'])
             .withPickerTitle('Pick an audio file'),
    folder: PathPickerConfig.createFolder(''),
    single: PathPickerConfig.createFile('').appendFileType('Executable', '*.exe'),
  };
}

// 只 print、什么都不改：跑一次就能看见喂进来的值，不必去动工程。
function main(inputs) {
  print('audio=[' + inputs.audio + '] folder=[' + inputs.folder + '] single=[' + inputs.single + ']');
}
'@, (New-Object Text.UTF8Encoding $false))

# 扩展夹具（V1.Settings）装进沙盒的 Extensions/，不碰 %APPDATA%。缺就是前置没备好，明说而不是静默跳过。
$tlx = Join-Path $repoRoot "tests/tlx/v1-settings.tlx"
$hasExtension = Test-Path $tlx
if ($hasExtension) {
    $extDir = Join-Path $dataDir "Extensions/v1-settings"
    New-Item -ItemType Directory -Force -Path $extDir | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($tlx, $extDir)
}

try {
    # ── 1. 声明 → 给模型看的类型措辞。这是 PathPickerConfig 在命令面上唯一的产出（ConfigText.Describe），
    #      也是模型据以决定"该传什么"的全部依据：说成 text 它就会瞎编一个路径，说成 file path 它才知道要问。
    Write-Host "1. the schema says what kind of path it is"
    $r = Invoke-Cli @("--headless", "script", "inputs", "--name", "PathPickerProbe")
    Check "exit 0" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"
    Check "file target carries its filters" ($r.Out -match "audio: file path \(\*\.wav, \*\.mp3\)") $r.Out
    Check "folder target says folder, not file" ($r.Out -match "folder: folder path") $r.Out
    Check "appendFileType also takes a single string" ($r.Out -match "single: file path \(\*\.exe\)") $r.Out
    Check "the declared default comes through" ($r.Out -match 'default "C:/default\.wav"') $r.Out

    # ── 2. --json：同样的事实要能机器读（Data 不做任何文案替换）。
    Write-Host "2. --json carries the same facts"
    $r = Invoke-Cli @("--headless", "script", "inputs", "--name", "PathPickerProbe", "--json")
    $parsed = $null
    try { $parsed = $r.Out | ConvertFrom-Json } catch { }
    Check "stdout parses as JSON" ($null -ne $parsed) $r.Out
    Check "and the schema text is in it" ($null -ne $parsed -and $parsed.schemaText -match "folder path") $r.Out

    # ── 3. 送值 → 值真的到达 main。这条是整套里最要紧的：写坏的 inputs 曾经【静默失效】
    #      （脚本拿默认值照跑、回报仍说跑成功），所以断言必须落在"main 收到的那个值"上，而不是退出码。
    Write-Host "3. the value reaches main"
    $r = Invoke-Cli @("--headless", "--yes", "script", "run-saved", "--name", "PathPickerProbe", "--inputs", (CliJson '{"audio":"C:/music/take1.wav","folder":"C:/banks"}'))
    Check "exit 0" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"
    Check "the file path arrived verbatim" ($r.Out -match "audio=\[C:/music/take1\.wav\]") $r.Out
    Check "the folder path arrived verbatim" ($r.Out -match "folder=\[C:/banks\]") $r.Out
    Check "the untouched field fell back to its default" ($r.Out -match "single=\[\]") $r.Out

    # ── 4. 不给 inputs：全量补默认（main 读到的是声明的默认路径，而不是 undefined）。
    Write-Host "4. defaults fill in"
    $r = Invoke-Cli @("--headless", "--yes", "script", "run-saved", "--name", "PathPickerProbe")
    Check "exit 0" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"
    Check "the default path is what main got" ($r.Out -match "audio=\[C:/default\.wav\]") $r.Out

    # ── 5. Windows 路径的反斜杠陷阱：写坏的 JSON 必须当场报用法错（退出码 2），不许降级成"当没给"。
    #      路径字段正是最容易踩这一脚的地方，故在这条线上再钉一次。
    Write-Host "5. the backslash trap stays loud"
    $r = Invoke-Cli @("--headless", "--yes", "script", "run-saved", "--name", "PathPickerProbe", "--inputs", (CliJson '{"audio":"C:\music\take1.wav"}'))
    Check "exit 2 (usage error, not a silent no-op)" ($r.Code -eq 2) "exit $($r.Code) $($r.Out)"
    Check "and it points at the backslash" ($r.Err -match "backslash") $r.Err

    # ── 6. 扩展设置那条路：同一个 config 家族，另一个入口（读 schema / 写值 / 类型校验）。
    Write-Host "6. extension settings"
    if (-not $hasExtension) {
        Check "the V1.Settings fixture is packed" $false "missing $tlx - run: dotnet build tests/plugins/V1.Settings/V1.Settings.csproj -c Release -t:Rebuild; pwsh tests/pack-tlx.ps1"
    } else {
        $r = Invoke-Cli @("--headless", "extension", "settings", "--extension", "TLSettingsDemo")
        Check "exit 0" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"
        Check "the file field says file path + filters" ($r.Out -match "engine_path.*: file path \(\*\.exe, \*\.bat\)") $r.Out
        Check "the folder field says folder path" ($r.Out -match "voice_bank_dir.*: folder path") $r.Out

        $r = Invoke-Cli @("--headless", "--yes", "extension", "set-setting", "--extension", "TLSettingsDemo", "--key", "engine_path", "--value", "C:/engines/neutrino.exe")
        Check "writing a path succeeds" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"
        Check "and it says what changed" ($r.Out -match "engine_path") $r.Out

        $r = Invoke-Cli @("--headless", "extension", "settings", "--extension", "TLSettingsDemo")
        Check "the written path is now the current value" ($r.Out -match "C:/engines/neutrino\.exe") $r.Out

        # 宿主【不校验路径存在性】——合法与否只有插件知道（用户也可能先填一个还没装的路径）。
        $r = Invoke-Cli @("--headless", "--yes", "extension", "set-setting", "--extension", "TLSettingsDemo", "--key", "voice_bank_dir", "--value", "D:/nope/not/installed/yet")
        Check "a path that does not exist is still accepted" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"

        # 类型仍照 config 校验：路径字段吃文本，不吃布尔。
        $r = Invoke-Cli @("--headless", "--yes", "extension", "set-setting", "--extension", "TLSettingsDemo", "--key", "engine_path", "--value", "true")
        Check "a boolean is refused" ($r.Code -eq 1) "exit $($r.Code) $($r.Out)"
        Check "and says it wanted text" ($r.Err -match "is text; got a boolean") $r.Err
    }
}
finally {
    Remove-Item -Recurse -Force $sandbox -ErrorAction SilentlyContinue
}

Write-Host ""
if ($script:Failures -gt 0) {
    Write-Host "path-picker: $($script:Failures) check(s) failed."
    exit 1
}
Write-Host "path-picker: all checks passed."
exit 0
