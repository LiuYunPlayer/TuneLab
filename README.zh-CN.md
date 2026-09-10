# TuneLab
*[English](README.md) | 简体中文*

TuneLab 是一款可扩展的歌声合成编辑器。

它通过扩展支持多种合成引擎、多种工程格式与效果器。TuneLab 还内置脚本与 AI Agent，并已完整本地化为多种语言。
## 下载与安装
到 [Releases 页](https://github.com/LiuYunPlayer/TuneLab/releases/latest)下载最新版本。目前发布只提供 Windows x64；其他平台请自行从源码构建。

- **安装器** - `TuneLab-Setup-win-x64-v<版本号>.exe`。装到当前用户的 `%LocalAppData%\Programs\TuneLab`（无需管理员），可选关联 `.tlpx` / `.tlp` / `.tlx` 文件类型，并注册卸载项。装好的版本可以就地自我更新。
- **免安装包** - `TuneLab-win-x64-v<版本号>.zip`。解压到任意目录，运行 `TuneLab.exe` 即可。不往文件夹外写东西，也不关联文件类型、不自我更新。

两者都是框架依赖构建，需要 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)（x64）；没装的机器首次启动会被引导到微软的下载页。

二进制未做代码签名，首次启动 Windows 可能弹出 SmartScreen 提示（"Windows 已保护你的电脑"）。点**更多信息** -> **仍要运行**即可继续。
## 用户手册
界面各区、五种编辑工具的操作手势、侧栏、设置、快捷键、文件与扩展管理，见[用户手册](docs/user-manual.zh-CN.md)。手册随软件发布，在软件内按 `F1`（或菜单**帮助 → 用户手册**）即可打开同一份内容；内置 AI Agent 也会查它来回答用法问题。
## 命令行与外部工具
安装包里带着一个命令行：安装目录下的 `tunelab.cmd`（免安装包里是 `TuneLab.Cli.exe`）。它跑的是与内置 AI Agent **同一批命令**——读工程、跑脚本、改设置、触发编辑器动作，既可以连上你正开着的窗口，也可以 `--headless` 起一个无界面的实例在 CI 里跑。`tunelab mcp` 还能把这批命令按 MCP 协议交给支持 MCP 的 AI 客户端。

要连上正在运行的 TuneLab，先在**设置 → 通用**里打开**命令桥**（默认关）。用法见[用户手册第 17 章](docs/user-manual.zh-CN.md#17-命令行与外部工具)，设计见 [command-surface.md](docs/command-surface.md)。

## 扩展安装
把 `.tlx` 扩展包拖进编辑器，或在扩展侧边栏里安装。
## 扩展开发
你可以开发自己的扩展，例如合成引擎、工程格式、效果器与乐器。扩展的开发详见[插件开发指南](docs/plugin-development.zh-CN.md)。

# 翻译贡献者
| 语言 | 贡献者 |
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
