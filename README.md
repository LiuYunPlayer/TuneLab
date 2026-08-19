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
