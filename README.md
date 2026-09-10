# TuneLab
*English | [简体中文](README.zh-CN.md)*

TuneLab is an extensible singing voice synthesis editor.

Through extensions, it supports multiple synthesis engines, various project formats, and effects. TuneLab also provides built-in scripting and an AI agent, and is fully localized into many languages.
## Download and install
Get the latest build from the [Releases page](https://github.com/LiuYunPlayer/TuneLab/releases/latest). Releases currently ship Windows x64 only; on other platforms, build from source.

- **Installer** - `TuneLab-Setup-win-x64-v<version>.exe`. Installs into `%LocalAppData%\Programs\TuneLab` for the current user (no administrator rights needed), optionally associates the `.tlpx` / `.tlp` / `.tlx` file types, and registers an uninstall entry. An installed copy can update itself in place.
- **Portable** - `TuneLab-win-x64-v<version>.zip`. Unzip anywhere and run `TuneLab.exe`. Nothing is written outside the folder, and it neither associates file types nor updates itself.

Both are framework-dependent builds and need the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64); a machine without it is pointed to Microsoft's download page on first launch.

The binaries are not code-signed, so Windows may greet the first launch with a SmartScreen notice ("Windows protected your PC"). Choose **More info** -> **Run anyway** to continue.
## User manual
A full walkthrough of the editor - every area of the UI, the five editing tools and their mouse gestures, the sidebar, settings, shortcuts, files and extensions - is available in Chinese: [用户手册](docs/user-manual.zh-CN.md). An English translation is not written yet.
The manual ships with the app: press `F1` (or **Help -> User Manual**) to read the same content in-app, and the built-in AI agent consults it when answering how-to questions.
## Unattended install (scripts / AI agents)
The installer has a command line of its own: every checkbox in the wizard has a matching option, and the defaults are the same ones the wizard starts with. `TuneLab-Setup-win-x64-v<version>.exe -help` lists them all.

```powershell
# Runtime (the packages are framework-dependent, so this has to be there first)
winget install --id Microsoft.DotNet.DesktopRuntime.8 --silent --accept-package-agreements

# Grab the latest installer and clear the "downloaded from the internet" mark
$api = Invoke-RestMethod https://api.github.com/repos/LiuYunPlayer/TuneLab/releases/latest
$url = ($api.assets | Where-Object { $_.name -like 'TuneLab-Setup-win-x64-*.exe' }).browser_download_url
$exe = Join-Path $env:TEMP (Split-Path $url -Leaf)
Invoke-WebRequest $url -OutFile $exe; Unblock-File $exe

# Install without a window (defaults match the wizard; these switches are the usual CI set)
& $exe -silent -desktop-shortcut false -start-menu-shortcut false -file-assoc false -launch false

# Check it worked
& "$env:LOCALAPPDATA\Programs\TuneLab\tunelab.cmd" --headless app info
```

Exit codes: `0` ok, `1` the install failed, `2` wrong usage; a silent run also logs to `%temp%\TuneLab.Setup.log`. Uninstall with `-uninstall <install dir>`. TuneLab cannot be installed over itself while it is running - a silent run waits 20 seconds for it to close, then says so and exits rather than hanging.

Once installed, scripts and AI agents can get to work through `tunelab --headless …` **without the command bridge**; the bridge is only needed to drive the window the user has open, and only they can turn it on. The manual covers both in Chinese: [chapter 1](docs/user-manual.zh-CN.md#1-安装与启动) and [chapter 17](docs/user-manual.zh-CN.md#17-命令行与外部工具).

## Command line and external tools
The packages ship with a command line: `tunelab.cmd` in the install directory (`TuneLab.Cli.exe` in the portable zip). It runs the **same commands as the built-in AI agent** - read the project, run scripts, change settings, trigger editor actions - either against the window you have open, or with `--headless` against a windowless instance for CI. `tunelab mcp` serves those same commands to any MCP-capable AI client over stdin/stdout.

To reach a running TuneLab, first turn on the **command bridge** in **Settings -> General** (off by default). Usage is documented in the manual, chapter 17: [命令行与外部工具](docs/user-manual.zh-CN.md#17-命令行与外部工具); the design is in [command-surface.md](docs/command-surface.md).

## Extension installation
Drag a `.tlx` extension package into the editor, or install one from the extensions sidebar.
## Extension development
You can develop your own extensions, such as synthesis engines, project formats, effects, and instruments. See the [Plugin Development Guide](docs/plugin-development.md) for details.

# Translation contributor
| Lang | contributor |
|------|:-----------:|
|en-US|-|
|zh-CN|-|
|zh-TW|@justln1113|
|ja-JP|@sevenc-nanashi|
|ko-KR|@Su-Yong|
|es-US|@AnotherNN|
|pt-BR|@overdramatic|
|fr-FR|@LittleAcrasy|
|nl-NL|@RhelaRazer|
|it-IT|@sykhro|
|el-GR|@A-MAIN|
|ru-RU|@Ksauxion|
|uk-UA|@Ksauxion|
|de-DE|@RedBlackAka|
|sv-SE|@ItzIcoza|
|tr-TR|@kulisfy|
