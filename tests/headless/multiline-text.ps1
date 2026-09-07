# 多行文本（TextBoxConfig.WithMultiline）的无头自动化（配方见 docs/command-surface.md §8，模板同 smoke.ps1）。
#
#   pwsh tests/headless/multiline-text.ps1 [-Configuration Debug]
#
# 多行这件事的关键不在于"框长什么样"（那是人看的），而在于【值仍是一个 string、换行原样穿过整条链路】：
# JSON 实参 → PropertyValue → 脚本 main。这条链上任何一处把 \n 吞掉或拆成两条，界面看着都正常，
# 只有把值原样打出来才看得见。故断言落在 main 收到的那个字符串上。
# 控件本身（框高几行、换行、滚动、多选占位、单行↔多行切换）没有自动化路径，见 tests/MULTILINE-TEXT-TEST-CASES.md。
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
    Write-Host "multiline-text: no TuneLab.Cli in bin/$Configuration. Build it first: dotnet build TuneLab.sln -c $Configuration"
    exit 2
}

$sandbox = Join-Path ([IO.Path]::GetTempPath()) ("TuneLab.Multiline." + [Guid]::NewGuid().ToString("N").Substring(0, 8))
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

# object 参数得先把引号转义，否则 CommandLineToArgvW 会剥掉它们（详见 path-picker.ps1 同名函数的注释）。
function CliJson([string]$json) { return ($json -replace '"', '\"') }

# 带空格的实参也要自己加引号：Start-Process 把 ArgumentList 拼成【一行命令行】、不会替你加，
# 于是 "remember to warm up" 被拆成四个实参，命令报 unexpected argument "to"。
function CliArg([string]$value) { if ($value -match '\s') { return '"' + $value + '"' } return $value }

# 三个字段各测一件事：多行（默认值自带换行）、普通单行作对照、掩码 + 多行的冲突声明（掩码优先，仍报 masked）。
$scriptsDir = Join-Path $dataDir "Scripts"
New-Item -ItemType Directory -Force -Path $scriptsDir | Out-Null
[IO.File]::WriteAllText((Join-Path $scriptsDir "MultilineProbe.js"), @'
function getScriptInfo() { return { name: 'Multiline Probe', context: 'global' }; }

function getInputConfig(ctx) {
  return {
    notes: TextBoxConfig.create('one\ntwo').withMultiline(5),
    free: TextBoxConfig.create('').withMultiline(),
    title: TextBoxConfig.create(''),
    secret: TextBoxConfig.create('').withPassword().withMultiline(3),
  };
}

// 把换行换成 | 打出来：日志/回报是逐行的，真换行会把一条拆成多条，看不出到底收到了什么。
function main(inputs) {
  print('notes=[' + String(inputs.notes).split('\n').join('|') + '] title=[' + inputs.title + ']');
}
'@, (New-Object Text.UTF8Encoding $false))

$tlx = Join-Path $repoRoot "tests/tlx/v1-settings.tlx"
$hasExtension = Test-Path $tlx
if ($hasExtension) {
    $extDir = Join-Path $dataDir "Extensions/v1-settings"
    New-Item -ItemType Directory -Force -Path $extDir | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($tlx, $extDir)
}

try {
    # ── 1. 声明 → 给模型看的措辞。模型据此才知道这个字段的值里可以有换行；说成 text 它就不会往里放。
    Write-Host "1. the schema says it takes newlines"
    $r = Invoke-Cli @("--headless", "script", "inputs", "--name", "MultilineProbe")
    Check "exit 0" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"
    Check "a capped box says how tall it goes" ($r.Out -match "notes: multi-line text \(box shows up to 5 lines\)") $r.Out
    # 0（缺省）= 不封顶：措辞里就不该出现行数。顺带钉住无参调用这一形态。
    Check "an uncapped box says just multi-line text" ($r.Out -match "free: multi-line text\. default") $r.Out
    Check "the plain one still says just text" ($r.Out -match "title: text") $r.Out
    # 掩码与多行同时声明时以掩码为准（掩码没有多行形态）——措辞上也不能说成 multi-line。
    Check "password wins over multiline" ($r.Out -match "secret: text \(masked\)") $r.Out

    # ── 2. 送值 → 换行原样到达 main。这条是整套里最要紧的：链路上任何一处吞掉 \n，界面都照样好看。
    Write-Host "2. newlines survive the whole chain"
    $r = Invoke-Cli @("--headless", "--yes", "script", "run-saved", "--name", "MultilineProbe", "--inputs", (CliJson '{"notes":"alpha\nbeta\ngamma"}'))
    Check "exit 0" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"
    Check "all three lines arrived, in order" ($r.Out -match "notes=\[alpha\|beta\|gamma\]") $r.Out

    # ── 3. 不给 inputs：默认值（本身就带换行）照样全量补进 main。
    Write-Host "3. a default that itself has newlines"
    $r = Invoke-Cli @("--headless", "--yes", "script", "run-saved", "--name", "MultilineProbe")
    Check "exit 0" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"
    Check "the declared default came through whole" ($r.Out -match "notes=\[one\|two\]") $r.Out

    # ── 4. 扩展设置那条路：同一个 config 家族的另一个入口，措辞要一致。
    Write-Host "4. extension settings"
    if (-not $hasExtension) {
        Check "the V1.Settings fixture is packed" $false "missing $tlx - run: dotnet build tests/plugins/V1.Settings/V1.Settings.csproj -c Release -t:Rebuild; pwsh tests/pack-tlx.ps1"
    } else {
        $r = Invoke-Cli @("--headless", "extension", "settings", "--extension", "TLSettingsDemo")
        Check "exit 0" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"
        Check "the multiline field says so" ($r.Out -match "startup_notes.*: multi-line text") $r.Out
        Check "the single-line one is unchanged" ($r.Out -match "model_path.*: text") $r.Out
        Check "the secret one is unchanged" ($r.Out -match "api_key.*secret text") $r.Out

        # 值仍是普通文本：写得进、读得回（--value 收的是裸字符串，命令行里塞真换行不现实，故只验普通值）。
        $r = Invoke-Cli @("--headless", "--yes", "extension", "set-setting", "--extension", "TLSettingsDemo", "--key", "startup_notes", "--value", (CliArg "remember to warm up"))
        Check "writing succeeds" ($r.Code -eq 0) "exit $($r.Code) $($r.Err)"
        $r = Invoke-Cli @("--headless", "extension", "settings", "--extension", "TLSettingsDemo")
        Check "and reads back" ($r.Out -match "remember to warm up") $r.Out
    }
}
finally {
    Remove-Item -Recurse -Force $sandbox -ErrorAction SilentlyContinue
}

Write-Host ""
if ($script:Failures -gt 0) {
    Write-Host "multiline-text: $($script:Failures) check(s) failed."
    exit 1
}
Write-Host "multiline-text: all checks passed."
exit 0
