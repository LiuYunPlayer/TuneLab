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
